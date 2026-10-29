// ============================================================================
// AgentAudit.cs —— AI Agent 的本机审计日志（NDJSON 追加写）
// 职责：把「模型请求 / 工具调用 / 写操作确认」按行记录到 AppData/audit.ndjson，
//       供演示结束后追溯「Agent 到底改了什么」。
// 位置：与 LocalCache 同级目录，但**不参与** local-cache.json 的整体写回
//       —— 业务缓存每次都是全量覆盖，审计是只增不改的事件流，两者生命周期不同。
// 协作：被 AdminViewModel 的对话循环与工具执行调用；被 SettingsViewModel 读取大小/清理。
// 易踩坑：
//   1) 审计失败绝不能影响对话：所有 IO 异常只写 LastError，不抛。
//   2) AppendAllText 每次开一次文件句柄，单行 O(1)；这是演示项目可接受的取舍，
//      真要高频写入应换成常驻 StreamWriter（但会引入 flush/关闭时机问题）。
//   3) 轮转只在写入前看一次文件长度，不逐行扫描；只保留一份 .1 历史。
// ============================================================================

using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aster.Services;

/// <summary>一条审计事件。字段全部可选（除 ts / sessionId / kind），按事件类型填。</summary>
public sealed class AgentAuditEntry
{
    /// <summary>发生时间（带时区偏移）。</summary>
    [JsonPropertyName("ts")]
    public DateTimeOffset Ts { get; init; } = DateTimeOffset.Now;

    /// <summary>进程级会话标识：同一次运行的所有记录共享它。</summary>
    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = AgentAudit.SessionId;

    /// <summary>事件类型：request（一次模型请求）/ tool（一次工具执行）/ confirm（确认或取消）。</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "request";

    /// <summary>本次请求使用的模型名（仅 request）。</summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>端点主机名（不含路径与查询串，绝不记录 API Key）。</summary>
    [JsonPropertyName("baseUrlHost")]
    public string? BaseUrlHost { get; init; }

    /// <summary>工具名（仅 tool）。</summary>
    [JsonPropertyName("toolName")]
    public string? ToolName { get; init; }

    /// <summary>参数摘要：只保留关键标识，自由文本截断，绝不记录完整正文。</summary>
    [JsonPropertyName("argsSummary")]
    public string? ArgsSummary { get; init; }

    /// <summary>是否成功。request 表示 HTTP + 解析成功，tool 表示业务校验通过。</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; init; } = true;

    /// <summary>失败原因（已截断）。</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>耗时（毫秒）：request 为整轮流式，tool 为单次工具执行。</summary>
    [JsonPropertyName("durationMs")]
    public long? DurationMs { get; init; }

    /// <summary>写操作目标，如 customer:c3 / task:t2；查询类为空。</summary>
    [JsonPropertyName("target")]
    public string? Target { get; init; }

    /// <summary>确认结果：true=用户确认执行，false=取消/放弃/过期（仅 confirm）。</summary>
    [JsonPropertyName("confirmed")]
    public bool? Confirmed { get; init; }
}

/// <summary>NDJSON 审计门面：静态、串行、失败降级。</summary>
public static class AgentAudit
{
    /// <summary>审计文件名。与 local-cache.json 同目录，随 AppData/ 一起被 .gitignore 忽略。</summary>
    private const string FileName = "audit.ndjson";

    /// <summary>轮转后的历史文件名（只保留一份，覆盖写）。</summary>
    private const string ArchiveName = "audit.ndjson.1";

    /// <summary>超过该大小就轮转：1 MB 足够几千条记录，也不至于让文件无限增长。</summary>
    private const long MaxBytes = 1024 * 1024;

    /// <summary>进程级会话标识：8 位短串，写进每条记录。</summary>
    // 不做持久化：它是「这一次运行」的关联键，重启就该换新的。
    public static string SessionId { get; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>审计文件完整路径。</summary>
    public static string FilePath => Path.Combine(LocalCache.DirectoryPath, FileName);

    /// <summary>最近一次写入错误（成功时为 null）。与 LocalCache.LastError 同样是排查用，不上界面。</summary>
    public static string? LastError { get; private set; }

    /// <summary>审计文件当前大小（字节）；尚未生成时为 0。</summary>
    public static long SizeBytes
    {
        get
        {
            try
            {
                var info = new FileInfo(FilePath);
                return info.Exists ? info.Length : 0;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return 0;
            }
        }
    }

    /// <summary>审计文件是否已生成（设置页据此显示「存在 / 未生成」）。</summary>
    public static bool Exists
    {
        get
        {
            try { return File.Exists(FilePath); }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }
    }

    // 写请求体：null 字段不输出，避免每条记录都拖一串 "toolName": null。
    // UnsafeRelaxedJsonEscaping 让中文原样落盘（与 local-cache.json 的约定一致），方便直接打开看。
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>追加一条审计事件。失败不抛异常，只记 LastError。</summary>
    // 调用方：AdminViewModel 的请求循环（request）、ExecuteTool（tool）、确认/取消（confirm）。
    // 前提：LocalCache.DirectoryPath 可解析；目录不存在时本方法负责创建。
    // 做完之后：文件末尾多一行 JSON；超过 1 MB 时旧文件先被移到 .1。
    public static void Append(AgentAuditEntry entry)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(LocalCache.DirectoryPath);
                RotateIfNeeded();

                var line = JsonSerializer.Serialize(entry, Options);
                // 单行 + \n：NDJSON 的约定是「一行一个完整 JSON」，读取端可以逐行解析。
                File.AppendAllText(FilePath, line + "\n", new UTF8Encoding(false));
                LastError = null;
            }
            catch (Exception ex)
            {
                // 审计是旁路能力：磁盘满、权限不足都不该让对话中断。
                LastError = ex.Message;
            }
        }
    }

    /// <summary>清空审计日志（设置页「清理审计日志」）。同样失败不抛。</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            try
            {
                var path = FilePath;
                if (File.Exists(path)) File.Delete(path);
                var archive = Path.Combine(LocalCache.DirectoryPath, ArchiveName);
                if (File.Exists(archive)) File.Delete(archive);
                LastError = null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }
    }

    /// <summary>文件超过 MaxBytes 时，把当前文件整体挪成 .1（覆盖上一份）。</summary>
    // 只保留一份历史：演示场景关心「最近发生了什么」，不需要完整归档。
    private static void RotateIfNeeded()
    {
        var path = FilePath;
        if (!File.Exists(path)) return;

        var length = new FileInfo(path).Length;
        if (length <= MaxBytes) return;

        var archive = Path.Combine(LocalCache.DirectoryPath, ArchiveName);
        File.Move(path, archive, overwrite: true);
    }

    /// <summary>串行锁：Append / Clear / 轮转必须互斥，避免移到一半被追加。</summary>
    private static readonly object Gate = new();
}
