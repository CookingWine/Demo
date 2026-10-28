// ============================================================================
// AiChatService.cs —— OpenAI 兼容协议的流式对话客户端（全工程唯一的联网出口）
// 职责：把「对话消息 + 工具声明」POST 到 {BaseUrl}/chat/completions（stream=true），
//       逐行解析 SSE 增量，把 delta 文本经回调抛出，结束返回聚合结果
//       （完整文本 + tool_calls 调用清单 + 错误信息）。
// 位置：被 AdminViewModel（工作台对话）调用，处在「业务调度」与「HTTP 端点」之间的薄层。
// 协作：读 LocalCache.Data.Ai 拿 BaseUrl / ApiKey / Model；不写缓存、不碰任何 UI 控件
//       ——onDelta 回调发生在后台读流线程，调用方（VM）自己负责切回 UI 线程。
// 易踩坑：
//   1) 流式响应没有「一次读完」的时刻，HttpClient.Timeout 必须设为 Infinite，
//      否则长回复会在 100s 默认超时处被腰斩；改用 CancellationTokenSource 控时。
//   2) tool_calls 的增量可能被拆进多个 chunk（id / name / arguments 分批到达），
//      必须按 delta 里的 index 聚合，不能假定一次给全。
//   3) 所有异常都转成 StreamResult.Error 返回，绝不让网络错误把界面带崩。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Aster.Services;

/// <summary>一条发给模型的消息。role ∈ system / user / assistant / tool。</summary>
// 不直接复用 ViewModels.ChatMessage：那是视图模型（带 IsUser / IsAgent 派生属性），
// 这边只认协议字段。tool 消息必须带 ToolCallId，assistant 消息想回传「我调过哪些工具」
// 时填 ToolCalls（多轮工具调用的上下文回放依赖它，服务端要据此对上 tool_call_id）。
public sealed class AiChatMessage
{
    /// <summary>角色：system / user / assistant / tool。</summary>
    [JsonPropertyName("role")]
    public string Role { get; init; } = "user";

    /// <summary>正文。assistant 发起工具调用的那轮可能只有 ToolCalls 没有 Content。</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>role=tool 时必填：这条结果对应哪一次调用。</summary>
    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; init; }

    /// <summary>role=assistant 且发起了工具调用时回填，供下一轮请求回放上下文。</summary>
    [JsonPropertyName("tool_calls")]
    public List<AiToolCallPayload>? ToolCalls { get; init; }
}

/// <summary>一次工具调用的协议形态（assistant 消息里的声明 + 回放上下文都用它）。</summary>
public sealed class AiToolCallPayload
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("function")]
    public AiFunctionCall Function { get; init; } = new();
}

/// <summary>工具调用的函数名与 JSON 字符串参数（注意：协议里是字符串，不是对象）。</summary>
public sealed class AiFunctionCall
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; init; } = "{}";
}

/// <summary>工具声明：名字 / 描述 / JSON Schema 参数。由业务方（AdminViewModel）提供。</summary>
public sealed record AiToolDefinition(string Name, string Description, JsonElement Parameters);

/// <summary>模型的一次工具调用（聚合完成后的结果形态）。</summary>
public sealed record AiToolCall(string Id, string Name, string Arguments);

/// <summary>
/// 一轮流式请求的最终结果：Content 是聚合后的完整文本（无文本时为空串），
/// ToolCalls 非空表示模型要求执行工具；Error 非空表示这次请求失败（内容不可信）。
/// </summary>
public sealed record AiStreamResult(
    string Content,
    IReadOnlyList<AiToolCall> ToolCalls,
    string? FinishReason,
    string? Error)
{
    /// <summary>请求成功（HTTP 与解析都没出错）。</summary>
    public bool Ok => Error is null;

    /// <summary>模型要求执行工具（此时 Content 通常为空）。</summary>
    [JsonIgnore]
    public bool HasToolCalls => ToolCalls.Count > 0;
}

/// <summary>
/// OpenAI 兼容流式客户端。无状态：每次 StreamChatAsync 都是独立请求，可安全复用同一实例。
/// </summary>
// 为什么做成实例类而不是全 static：工具循环（VM 里）每次轮次都要 new 一批消息，
// 实例只是「服务的把柄」，真正的共享资源是下面这个 static HttpClient ——
// 每个进程一个 HttpClient 是官方建议（避免高频新建导致端口耗尽）。
public sealed class AiChatService
{
    /// <summary>单轮流式请求的最长时长：从发出请求到读完整个 SSE 流。</summary>
    // 120s 对一次「工具调用轮」足够（模型的思考与生成都在里面）；
    // 多轮工具循环由调用方每轮各起一次请求，超时不叠加。
    private static readonly TimeSpan StreamTimeout = TimeSpan.FromSeconds(120);

    // Timeout=Infinite 是流式的硬要求（见文件头）；真实时限靠每次请求的 linked CTS 兜。
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    // 读 SSE 里每个 data 行的 JSON：字段名固定小写，大小写不敏感只为容错。
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    // 写请求体：null 字段不输出（没有工具时就不该出现 "tools": null，某些网关会报错）。
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ---------------- 对外主入口 ----------------

    /// <summary>
    /// 流式对话一轮：增量文本经 <paramref name="onDelta"/>（后台线程）抛出，
    /// 结束返回聚合结果。失败时不抛异常，Error 里带可读原因。
    /// </summary>
    // 谁调用：AdminViewModel 的对话循环（每轮工具调用各调一次）。
    // 前提：LocalCache.Data.Ai 已配置（ApiKey 为空会直接返回错误结果）。
    // 做完之后：返回的 ToolCalls 若非空，由调用方执行工具并以 tool 消息追加进下一轮请求。
    // modelOverride：会话级模型选择（工作台输入框下拉），非空时覆盖设置里的全局默认；
    //                留空/空白走 cfg.Model，老调用方（只传四个参数）行为完全不变。
    public async Task<AiStreamResult> StreamChatAsync(
        IReadOnlyList<AiChatMessage> messages,
        IReadOnlyList<AiToolDefinition>? tools,
        Action<string>? onDelta,
        CancellationToken ct,
        string? modelOverride = null)
    {
        var cfg = LocalCache.Data.Ai;
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.ApiKey))
            return Fail("尚未配置 AI 模型的 API Key，请在「设置 → AI 模型」里填写。");

        // 模型取值优先级：会话级覆盖 > 设置页全局默认 > 内置兜底。
        var requested = string.IsNullOrWhiteSpace(modelOverride) ? cfg.Model : modelOverride;
        var model = string.IsNullOrWhiteSpace(requested) ? "deepseek-chat" : requested.Trim();
        var url = BuildCompletionsUrl(cfg.BaseUrl);

        // ---------- 组装请求体 ----------
        // tools 的协议形态：[{ type: "function", function: { name, description, parameters } }]。
        object body = tools is { Count: > 0 }
            ? new { model, messages, stream = true, tools = WrapTools(tools) }
            : new { model, messages, stream = true };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(StreamTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
            // Bearer 鉴权是 OpenAI 兼容协议的统一约定（Ollama 没有 Key 时随便填个非空串也能过）。
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cfg.ApiKey.Trim());
            request.Content = new StringContent(JsonSerializer.Serialize(body, WriteOptions), Encoding.UTF8, "application/json");

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);

            // 非 2xx：把响应体读出来（通常含 error.message），拼上状态码方便排查。
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
                return Fail($"请求失败（HTTP {(int)response.StatusCode}）：{ExtractErrorMessage(detail, response.StatusCode)}");
            }

            return await ReadStreamAsync(response.Content, onDelta, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 只有超时会把 linked CTS 取消而外部 token 未取消；用户主动取消走下一个分支。
            return Fail("请求超时（120 秒未完成），请检查网络或稍后再试。");
        }
        catch (OperationCanceledException)
        {
            return Fail("已取消。");
        }
        catch (HttpRequestException ex)
        {
            return Fail($"网络错误：{ex.Message}（请检查 BaseUrl 与网络连接）");
        }
        catch (Exception ex)
        {
            // 兜底：解析异常、编码异常等一切未预期错误都转成可读文本，不让界面崩。
            return Fail($"AI 请求出错：{ex.Message}");
        }
    }

    // ---------------- SSE 流读取与解析 ----------------

    /// <summary>逐行读响应流：每行形如 "data: {...}"，"[DONE]" 表示结束。</summary>
    // SSE 的行结构按协议以 \n 分隔，StreamReader.ReadLine 足够；行与行之间的空行直接跳过。
    // 这里逐行处理而不是整段缓冲：响应可能非常长，逐行是 O(1) 内存外的增量处理。
    private static async Task<AiStreamResult> ReadStreamAsync(HttpContent content, Action<string>? onDelta, CancellationToken ct)
    {
        var text = new StringBuilder();                                  // 聚合完整回复
        var calls = new Dictionary<int, PendingCall>();                  // 按 index 聚合工具调用
        string? finish = null;

        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line)) continue;

            // 只认 "data:" 前缀（有的实现带空格有的不带，Trim 后统一判断）；
            // 注释行（":"开头）与 event/id 等其它字段直接忽略。
            line = line.Trim();
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var payload = line["data:".Length..].Trim();
            if (payload.Length == 0) continue;
            if (payload == "[DONE]") break;

            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;

                // 部分网关在 200 流里也会给 error 对象，见到就当失败收场。
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var err))
                {
                    return Fail($"服务端返回错误：{err.ToString()}");
                }

                if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    continue;
                var choice = choices[0];

                if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
                    finish = fr.GetString();

                if (choice.TryGetProperty("delta", out var delta))
                {
                    // 1) 文本增量：抛给回调 + 聚合。
                    if (delta.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
                    {
                        var piece = contentEl.GetString();
                        if (!string.IsNullOrEmpty(piece))
                        {
                            text.Append(piece);
                            onDelta?.Invoke(piece);
                        }
                    }

                    // 2) 工具调用增量：按 index 聚合，id / name / arguments 都可能分片到达。
                    if (delta.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tc in tcs.EnumerateArray())
                        {
                            var index = tc.TryGetProperty("index", out var idxEl) && idxEl.ValueKind == JsonValueKind.Number
                                ? idxEl.GetInt32()
                                : calls.Count;
                            if (!calls.TryGetValue(index, out var pending))
                            {
                                pending = new PendingCall();
                                calls[index] = pending;
                            }

                            if (tc.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                                pending.Id = idEl.GetString() ?? pending.Id;

                            if (tc.TryGetProperty("function", out var fn))
                            {
                                if (fn.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                                    pending.Name.Append(nameEl.GetString());
                                if (fn.TryGetProperty("arguments", out var argEl) && argEl.ValueKind == JsonValueKind.String)
                                    pending.Args.Append(argEl.GetString());
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // 单个 data 行解析失败（网关插入的心跳/杂质行）跳过即可，别让整条流报废。
            }
        }

        // 按模型给出的 index 排序，保证多工具调用的执行顺序与模型意图一致。
        var sortedIndexes = new List<int>(calls.Keys);
        sortedIndexes.Sort();
        var result = new List<AiToolCall>();
        foreach (var index in sortedIndexes)
        {
            var pending = calls[index];
            result.Add(new AiToolCall(
                string.IsNullOrEmpty(pending.Id) ? $"call_{index}" : pending.Id,
                pending.Name.ToString(),
                pending.Args.Length == 0 ? "{}" : pending.Args.ToString()));
        }

        return new AiStreamResult(text.ToString(), result, finish, null);
    }

    // ---------------- 辅助 ----------------

    /// <summary>把 BaseUrl 规整成完整的 chat/completions 端点。</summary>
    // 容忍三种写法：https://api.deepseek.com、https://api.deepseek.com/、
    // https://dashscope.aliyuncs.com/compatible-mode/v1 —— 统一在末尾补 /chat/completions。
    private static string BuildCompletionsUrl(string? baseUrl)
    {
        var root = string.IsNullOrWhiteSpace(baseUrl) ? "https://api.deepseek.com" : baseUrl.Trim();
        return root.TrimEnd('/') + "/chat/completions";
    }

    /// <summary>把业务工具声明包装成协议要求的 { type, function } 形态。</summary>
    private static List<object> WrapTools(IReadOnlyList<AiToolDefinition> tools)
    {
        var list = new List<object>(tools.Count);
        foreach (var t in tools)
            list.Add(new { type = "function", function = new { name = t.Name, description = t.Description, parameters = t.Parameters } });
        return list;
    }

    /// <summary>从错误响应体里抠出人话（OpenAI 兼容错误一般是 {"error":{"message":...}}）。</summary>
    private static string ExtractErrorMessage(string body, System.Net.HttpStatusCode status)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var err) &&
                    err.TryGetProperty("message", out var msg) &&
                    msg.ValueKind == JsonValueKind.String)
                    return msg.GetString() ?? body;
            }
            catch (JsonException)
            {
                // 不是 JSON 就原样返回（截断到 200 字符，防止一行 HTML 刷屏）。
            }
            return body.Length > 200 ? body[..200] : body;
        }
        return $"HTTP {(int)status}";
    }

    private static AiStreamResult Fail(string message) =>
        new(string.Empty, Array.Empty<AiToolCall>(), "error", message);

    /// <summary>单个工具调用的聚合中间态：id 是覆盖写，name / arguments 是追加拼。</summary>
    private sealed class PendingCall
    {
        public string Id { get; set; } = string.Empty;
        public StringBuilder Name { get; } = new();
        public StringBuilder Args { get; } = new();
    }
}
