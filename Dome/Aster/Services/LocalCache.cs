// ============================================================================
// LocalCache.cs —— 本地缓存：全工程唯一的「本地数据存储」入口
// 职责：把一个 JSON 文件（local-cache.json）当作轻量本地数据库，负责
//       ① 定位文件位置（开发态放仓库内的 AppData/，发布态放用户漫游目录）；
//       ② 首次启动自动建文件（带默认结构），损坏时自动备份并重建；
//       ③ 读（Data 惰性加载一次，之后常驻内存）、写（原子写 + 延迟合并写）。
// 位置：被 ViewModels 与 Views 直接调用，处在「界面状态」与「磁盘」之间的薄层。
// 协作：AdminViewModel 存对话与审批结果；MainWindowViewModel 存「记住我」的账号；
//       MainWindow 存窗口尺寸；SettingsViewModel 展示数据目录与缓存占用。
// 易踩坑：
//   1) 这个文件是运行时数据，必须被 .gitignore 忽略（见仓库根 .gitignore），
//      不要把里面的内容提交上去 —— 它含账号名与操作记录；
//   2) 所有写操作都吞掉异常：缓存写失败绝不能让界面崩，最坏只是「这次没记住」；
//   3) SaveSoon 是「延迟合并写」，一次操作里连发 N 条通知只会落一次盘；
//      需要立刻持久化（如登录成功）时用 Save。
// ============================================================================
//
// ── 文件长什么样 ──────────────────────────────────────────────────────────
// 一份缩进过的 UTF-8 JSON，中文不转义（中文直接可读，方便手工排查与维护）：
//
//   {
//     "schemaVersion": 2,
//     "appVersion": "1.0.0.0",
//     "updatedAt": "2026-10-25T19:20:12.345+08:00",
//     "login":  { "remember": true, "userName": "张客服", "lastLoginAt": "..." },
//     "window": { "width": 1320, "height": 850 },
//     "workspace": {
//       "customers": [ { "id": "c1", "name": "...", "code": "KH-001", "people": 68,
//                        "activities": [...], "staff": [...], "runs": [...], "files": [...] } ],
//       "tasks":     [ { "id": "t1", "title": "...", "kind": "审批", "status": "待处理" } ],
//       "metrics":   [ { "title": "任务处理", "value": "0 / 12" } ],
//       "messages":  [ { "author": "张客服", "text": "...", "isUser": true } ]
//     }
//   }
//
// 四条约定：
//   · schemaVersion 不匹配 → 整份丢弃重建（演示项目不做逐版本迁移，见 LoadFromDisk）；
//   · workspace 下的四个列表都是「全量快照」而非增量：每次保存整体覆盖，读取时直接回放；
//   · 时间统一 DateTimeOffset（带时区偏移），换时区打开也不会错位；
//   · 文件里的字段名全部 camelCase，与 C# 的 PascalCase 属性靠命名策略映射。
//
// ── 这份文件是工作区界面的唯一数据源 ──────────────────────────────────────
// v2 起工作区不再有任何硬编码演示数据：客户、客户详情（最近更新 / 人员 / 核对 / 文件）、
// 审批待办、KPI 指标、对话记录全部从 workspace 读出来渲染。
// 也就是说「界面长什么样」完全由这个文件决定 —— 想清空就把它清空，想造数据就手工往里写，
// 程序启动时会原样读出来画（见 AdminViewModel.LoadWorkspace）。
//
// ── 为什么不用数据库 ──────────────────────────────────────────────────────
// 工程零第三方依赖（见 README「技术说明」），System.Text.Json 已在共享框架里，
// 数据量又是「几十条对话 + 几条客户」，一个 JSON 文件足够；
// 换成 SQLite 会引入原生库，把「clone 下来就能跑」这条底线破坏掉。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using Avalonia.Threading;

namespace Aster.Services;

/// <summary>
/// 缓存文件的根对象：一份 LocalCacheData 就是磁盘上一个 local-cache.json。
/// 全部属性都是可读可写（不是 init-only）：反序列化要靠 setter 赋值，
/// 且运行期会反复改其中几项（Login / Window / Workspace）。
/// </summary>
// 子对象刻意都给了默认实例而不是 null：新用户第一次启动时直接可用，
// 调用方也不必到处写 ?. —— 唯一的代价是反序列化遇到 "login": null 时
// 会真的塞一个 null 进来，所以 Normalize 里还会兜一次底。
public sealed class LocalCacheData
{
    /// <summary>结构版本。与 LocalCache.CurrentSchemaVersion 不一致时整份重建。</summary>
    public int SchemaVersion { get; set; } = LocalCache.CurrentSchemaVersion;

    /// <summary>写入这份缓存的程序版本，排查「旧版本写坏的结构」时用。</summary>
    public string AppVersion { get; set; } = LocalCache.AppVersion;

    /// <summary>最后一次落盘时间，由 Save 覆盖，不需要调用方维护。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>登录相关的记忆项（对应登录页的「记住我」）。</summary>
    public LoginCache Login { get; set; } = new();

    /// <summary>上一次正常状态下的窗口尺寸。</summary>
    public WindowCache Window { get; set; } = new();

    /// <summary>AI 模型接入配置（OpenAI 兼容协议的端点、密钥与模型名）。</summary>
    public AiConfigCache Ai { get; set; } = new();

    /// <summary>工作区的全部展示数据 —— 界面画什么完全由它决定。</summary>
    public WorkspaceCache Workspace { get; set; } = new();
}

/// <summary>
/// 工作区数据：客户（含四个详情页签的数据）、审批待办、KPI 指标、对话记录。
/// </summary>
// 四个列表都是「全量快照」：程序启动时整体读出、界面上改了就整体写回。
// 默认全是空列表 —— 空列表就是「没有数据」的合法状态，界面会走各自的空态提示，
// 不会抛异常，也不会偷偷塞演示数据。
public sealed class WorkspaceCache
{
    /// <summary>客户档案列表（客户工作空间 / 客户详情的数据源）。</summary>
    public List<CustomerCache> Customers { get; set; } = new();

    /// <summary>审批 / 待办列表（协作与审批页的数据源）。</summary>
    public List<TaskCache> Tasks { get; set; } = new();

    /// <summary>KPI 指标列表（KPI 与考勤页的数据源）。</summary>
    public List<MetricCache> Metrics { get; set; } = new();

    /// <summary>工作台对话记录。</summary>
    public List<MessageCache> Messages { get; set; } = new();
}

/// <summary>
/// 一个客户的全部数据：档案字段 + 详情页四个页签的行数据。
/// </summary>
// 详情数据（最近更新 / 人员 / 核对 / 文件）之所以跟着客户一起存，而不是在界面上按档案字段
// 现算出来：v2 的原则是「界面只负责画，不负责编数据」。想改某个客户的核对记录，
// 直接改这个客户的 runs 数组即可，不用去碰代码。
public sealed class CustomerCache
{
    /// <summary>客户 Id（深链与后续扩展用；界面目前不做唯一性校验）。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>客户全称，列表首列与详情页标题都用它。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>客户简称，用于拼文件名与人员行里的「所属客户」。</summary>
    public string Short { get; set; } = string.Empty;

    /// <summary>客户编号，形如 KH-001。</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>服务类型：劳务派遣 / 业务外包 / 人事代理。</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>负责人（也用于「我负责的客户」筛选，按 "张客服" 匹配）。</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>所在城市。</summary>
    public string City { get; set; } = string.Empty;

    /// <summary>客户联系人（演示数据里是脱敏代号）。</summary>
    public string Contact { get; set; } = string.Empty;

    /// <summary>服务内容，一行文案。</summary>
    public string Service { get; set; } = string.Empty;

    /// <summary>税务登记状态。</summary>
    public string Tax { get; set; } = string.Empty;

    /// <summary>交接材料文件名。</summary>
    public string File { get; set; } = string.Empty;

    /// <summary>最后更新时间的展示文案，如 "10-17 10:32"（刻意存字符串，不做本地化转换）。</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>核对状态：待复核 / 已完成 / 待补材料 / 暂无核对。决定列表标签配色。</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>在册人数。</summary>
    public int People { get; set; }

    /// <summary>待处理事项数量（0 时列表显示破折号）。</summary>
    public int Pending { get; set; }

    /// <summary>详情页「最近更新」条目。</summary>
    public List<ActivityCache> Activities { get; set; } = new();

    /// <summary>详情页「人员记录」行。</summary>
    public List<StaffCache> Staff { get; set; } = new();

    /// <summary>详情页「核对记录」行。</summary>
    public List<RunCache> Runs { get; set; } = new();

    /// <summary>详情页「文件与产出」行。</summary>
    public List<FileEntryCache> Files { get; set; } = new();
}

/// <summary>详情页「最近更新」的一条记录。</summary>
public sealed class ActivityCache
{
    public string Text { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
}

/// <summary>详情页「人员记录」的一行。</summary>
public sealed class StaffCache
{
    public string No { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string IdInfo { get; set; } = "已脱敏";
    public string Customer { get; set; } = string.Empty;
    public string Status { get; set; } = "在册";
    public string Source { get; set; } = string.Empty;
}

/// <summary>详情页「核对记录」的一行。</summary>
public sealed class RunCache
{
    public string Batch { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Pending { get; set; } = "—";
    public string Date { get; set; } = string.Empty;
}

/// <summary>详情页「文件与产出」的一行。</summary>
public sealed class FileEntryCache
{
    public string Name { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string Related { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;

    /// <summary>标签语气：ok（Excel 产出，绿色）/ muted（原始材料，灰色）。</summary>
    public string Tone { get; set; } = "muted";
}

/// <summary>登录记忆：勾了「记住我」才会保留账号名。</summary>
// 只存账号名，不存密码 —— 哪怕项目其它地方把密码明文放在内存里，
// 也绝不落盘，这是这个文件里唯一一条不可让步的规则。
public sealed class LoginCache
{
    /// <summary>「记住我」是否勾选。false 时 UserName 会被清空。</summary>
    public bool Remember { get; set; } = true;

    /// <summary>被记住的账号名，下次启动直接回填登录框。</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>最后一次登录成功的时间，目前只在文件里留痕，界面上没有展示。</summary>
    public DateTimeOffset? LastLoginAt { get; set; }
}

/// <summary>一条对话记录的持久化形态（对应 ViewModels.ChatMessage）。</summary>
// 为什么不直接序列化 ChatMessage：那是视图模型，属性是 init-only 且带派生属性
// （IsAgent = !IsUser），直接序列化会把派生值也写进文件、读回来又对不上。
// 存储层只认「原始字段」，转换在 AdminViewModel 里显式做，两边互不污染。
public sealed class MessageCache
{
    /// <summary>发送者显示名（"Agent" / "张客服"）。</summary>
    public string Author { get; set; } = "Agent";

    /// <summary>消息正文。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>true = 用户发的（右侧蓝气泡），false = Agent 发的（左侧无气泡）。</summary>
    public bool IsUser { get; set; }
}

/// <summary>一条审批 / 待办事项（对应 ViewModels.TaskItem）。</summary>
// v1 里这里只存 id + status + note（其余字段写死在代码里）；v2 起整条任务都在文件里，
// 包括标题与所属客户 —— 界面上再也没有「代码里写死、文件里只存状态」的半吊子状态。
public sealed class TaskCache
{
    /// <summary>任务 Id，界面上不可见，只在写回时用来对齐（多条同名任务靠它区分）。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>任务标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>类型：审批 / 核对 / 补材料 / 资料。</summary>
    public string Kind { get; set; } = "审批";

    /// <summary>所属客户名（纯展示，不做外键校验）。</summary>
    public string Customer { get; set; } = string.Empty;

    /// <summary>期限文案：今日 / 待安排。</summary>
    public string Due { get; set; } = "今日";

    /// <summary>状态：待处理 / 已通过 / 已退回。只有「待处理」会出现在默认筛选里。</summary>
    public string Status { get; set; } = "待处理";

    /// <summary>处理说明，退回时会写上原因。</summary>
    public string Note { get; set; } = string.Empty;
}

/// <summary>一条 KPI 指标（对应 ViewModels.MetricRow）。</summary>
public sealed class MetricCache
{
    public string Title { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

/// <summary>窗口尺寸记忆（只记尺寸，不记位置与最大化状态）。</summary>
// 不记位置是刻意的：换显示器 / 改分辨率后旧坐标可能落在屏幕外，
// 而「居中 + 记住尺寸」在任何屏幕组合下都不会把窗口开丢。
// 0 表示「没有有效记录」，MainWindow 会退回 XAML 里的设计稿尺寸。
public sealed class WindowCache
{
    /// <summary>上次关闭时的窗口宽度（逻辑像素）；0 表示未记录。</summary>
    public double Width { get; set; }

    /// <summary>上次关闭时的窗口高度（逻辑像素）；0 表示未记录。</summary>
    public double Height { get; set; }
}

/// <summary>
/// AI 模型接入配置：OpenAI 兼容协议的端点、密钥与模型名。
/// ApiKey 为空 = 未配置，工作台对话退回「未配置」提示，不发请求。
/// </summary>
// BaseUrl 存「根地址」而不是完整的 /chat/completions 路径：
// DeepSeek 是 https://api.deepseek.com、通义兼容模式是 https://dashscope.aliyuncs.com/compatible-mode/v1、
// Ollama 是 http://localhost:11434/v1，各家的路径前缀本来就不一样，拼路径的责任放在
// AiChatService 里（自动补 /chat/completions，容忍末尾斜杠）。
// 密钥明文落盘是与 local-cache.json 同级的取舍：文件含账号名与操作记录且已被 .gitignore 忽略，
// 本机留存即可；该文件规则「不存密码」仅针对登录密码，AI Key 是用户自己填的服务凭据，
// 想清掉走设置页或删文件。默认值指向 DeepSeek，与 README 的演示语境一致。
public sealed class AiConfigCache
{
    /// <summary>API 密钥；空串表示未配置。</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>OpenAI 兼容端点的根地址，如 https://api.deepseek.com。</summary>
    public string BaseUrl { get; set; } = "https://api.deepseek.com";

    /// <summary>模型名，如 deepseek-chat / deepseek-reasoner / qwen-plus。</summary>
    public string Model { get; set; } = "deepseek-chat";
}

/// <summary>
/// 本地缓存的读写门面。全部 static：整个进程只需要一份缓存，没有多实例的理由。
/// </summary>
// 读写模型是「内存优先」：第一次访问 Data 时把文件读进内存，之后所有读取都走内存；
// 写入不是即时落盘，而是打上脏标记后由 700ms 的防抖计时器合并成一次写。
// 这样 Search 输入框那种「每敲一个字就改状态」的场景不会产生一串小文件写。
public static class LocalCache
{
    /// <summary>当前结构版本。改动 LocalCacheData 的字段含义时必须 +1。</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>缓存文件名。改名字会让旧文件变成孤儿（不自动删除，需手工清理）。</summary>
    private const string FileName = "local-cache.json";

    /// <summary>开发态下的数据目录名（与 Aster.csproj 同级）。</summary>
    private const string DevFolder = "AppData";

    /// <summary>延迟合并写的等待时长：一次连续操作结束 700ms 后落盘。</summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(700);

    // 锁只在「加载 / 写盘」两个临界区里持有，且持有时不做任何 UI 操作，
    // 所以不会出现「等锁等出死锁」的情况。VM 的每次改动都只是改内存对象，不竞争这把锁。
    private static readonly object Gate = new();

    // 序列化选项：
    //   WriteIndented         —— 缩进，方便直接用编辑器打开看；
    //   CamelCase             —— C# 的 Login → JSON 的 login；
    //   UnsafeRelaxedJsonEscaping —— 中文与中文引号原样输出，不做 \uXXXX 转义；
    //   Skip 空值             —— 文件里不出现一堆 null 字段。
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static LocalCacheData? _data;
    private static string? _directory;
    private static DispatcherTimer? _saveTimer;

    /// <summary>程序集版本，写进文件的 appVersion 字段。</summary>
    public static string AppVersion { get; } =
        typeof(LocalCache).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    /// <summary>缓存所在目录（不存在时按需创建，本属性本身不建目录）。</summary>
    public static string DirectoryPath => _directory ??= ResolveDirectory();

    /// <summary>缓存文件完整路径。设置页「数据目录」显示的是它的上级目录。</summary>
    public static string FilePath => Path.Combine(DirectoryPath, FileName);

    /// <summary>最近一次读写的错误信息（成功时为 null）。界面上没有展示，排查时用。</summary>
    public static string? LastError { get; private set; }

    /// <summary>
    /// 缓存数据（惰性加载）。拿到的是同一个可变实例，改完属性后要调 Save 或 SaveSoon。
    /// </summary>
    // 这是全工程访问缓存的主入口：调用方写 LocalCache.Data.Xxx 就够了，
    // 不需要关心「有没有读过盘」「目录在哪」这些细节。
    public static LocalCacheData Data
    {
        get
        {
            // 双检的目的只是省一次锁：绝大多数调用发生在加载完成之后。
            var data = _data;
            return data ?? EnsureLoaded();
        }
    }

    /// <summary>确保缓存已加载（重复调用只读一次盘），返回常驻内存的实例。</summary>
    // 调用点：Data 的 getter、MainWindow 构造（要在首帧前拿到窗口尺寸）、
    //        以及设置页初始化时读取文件大小。
    public static LocalCacheData EnsureLoaded()
    {
        lock (Gate)
        {
            return _data ??= LoadFromDisk();
        }
    }

    /// <summary>立即落盘（同步）。需要「这一刻就必须记住」的场景用它，比如登录成功。</summary>
    // 内部一定是先补时间戳再写，保证文件里的 updatedAt 反映的是这次写盘而不是上次。
    public static void Save()
    {
        lock (Gate)
        {
            // 还没加载过说明没人改过内存，此时只要保证文件存在即可。
            var data = _data ??= LoadFromDisk();
            data.UpdatedAt = DateTimeOffset.Now;
            WriteToDisk(data);
        }
    }

    /// <summary>
    /// 延迟合并写：700ms 内的多次调用只会真正落盘一次。
    /// 必须在 UI 线程调用（内部用 DispatcherTimer）；非 UI 线程会被自动转到 UI 线程。
    /// </summary>
    // 为什么不干脆每次都 Save：审批页一次「退回」会连着改 Status、Note、重建集合，
    // 搜索框更是每敲一个字就改一次状态，逐次落盘既慢又制造无用写。
    public static void SaveSoon()
    {
        // DispatcherTimer 必须在有 Dispatcher 的线程上创建，非 UI 线程先跳回去。
        // CheckAccess 为 false 只会出现在后台任务里，当前代码路径都发生在 UI 线程，
        // 保留这个分支是为了将来把缓存写挪到后台时不至于抛异常。
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(SaveSoon);
            return;
        }

        // 计时器惰性创建：LocalCache 可能在 Avalonia 初始化完成前被访问（如静态字段求值），
        // 那时 new DispatcherTimer 会拿不到 Dispatcher，所以放到第一次真正需要时再建。
        _saveTimer ??= CreateSaveTimer();
        // Stop + Start 是「重置防抖窗口」的标准写法：
        // 每次调用都把计时推后 700ms，连续操作只有最后一次能等到 Tick。
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>
    /// 清空缓存：内存回到默认结构，磁盘文件被清空后立即按默认结构重建。
    /// 注意运行期的界面状态不会被回滚 —— 那些状态已经复制到了各 ViewModel 里。
    /// </summary>
    // 设置页「存储与备份 → 缓存占用 → 清理」绑的就是它（见 SettingsViewModel.BuildStorage）。
    // 删了再写而不是只删：保证「文件一定存在」这条不变量，下次加载不会走「文件缺失」分支。
    public static void Reset()
    {
        lock (Gate)
        {
            _saveTimer?.Stop();
            _data = new LocalCacheData();
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch (Exception ex)
            {
                // 删不掉（被占用 / 没权限）不算致命：下面会整体覆盖写，效果一致。
                LastError = ex.Message;
            }
            WriteToDisk(_data);
        }
    }

    /// <summary>把缓存文件的大小格式化成「local-cache.json · 1.8 KB」，供设置页展示。</summary>
    public static string Describe()
    {
        try
        {
            var length = new FileInfo(FilePath).Length;
            var size = length < 1024
                ? $"{length} B"
                : length < 1024 * 1024
                    ? $"{length / 1024d:0.0} KB"
                    : $"{length / 1024d / 1024d:0.0} MB";
            return $"{FileName} · {size}";
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return FileName;
        }
    }

    // ---------- 内部实现 ----------

    /// <summary>
    /// 决定缓存放在哪：环境变量 &gt; 仓库内 AppData/ &gt; 用户漫游目录。
    /// </summary>
    // 三档优先级对应三种使用场景：
    //   ① ASTER_DATA_DIR：想在别处跑一份隔离数据时（比如演示前清空、或把数据放到网盘）；
    //   ② 从可执行文件目录往上找 Aster.csproj —— 找到说明这是 `dotnet run` / `dotnet build`
    //      的开发态，缓存落在仓库里的 AppData/（该目录已在 .gitignore 中，不会推上 GitHub）；
    //   ③ 都找不到说明是发布后的安装包（publish 输出里没有 csproj），
    //      这时写进当前用户的 Roaming 目录，与「设置 → 数据目录」显示的位置一致。
    // 注意：开发态和发布态用的是两个不同的文件，这是有意为之 ——
    // 调试时改坏的数据不该污染用户环境，发布包的写入也不该落进只读的安装目录。
    private static string ResolveDirectory()
    {
        var custom = Environment.GetEnvironmentVariable("ASTER_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            try
            {
                return Path.GetFullPath(custom);
            }
            catch (Exception)
            {
                // 路径非法（含非法字符）就当作没设置，继续往下走。
            }
        }

        // 从 bin/Debug/net8.0 往上最多找几层就够：Aster.csproj 在上两级。
        // 用 while 而不是固定层数，是为了兼容 `dotnet run` 与 `dotnet publish -o` 的不同深度。
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Aster.csproj")))
                return Path.Combine(dir.FullName, DevFolder);
            dir = dir.Parent;
        }

        // GetFolderPath 在极少数环境会返回空串，此时退回当前目录，至少不会抛异常。
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return string.IsNullOrEmpty(roaming)
            ? Path.Combine(Directory.GetCurrentDirectory(), "AsterCache")
            : Path.Combine(roaming, "Aster", "Cache");
    }

    /// <summary>建一个一次性防抖计时器（只负责在自己身上 Stop）。</summary>
    private static DispatcherTimer CreateSaveTimer()
    {
        var timer = new DispatcherTimer { Interval = SaveDelay };
        timer.Tick += (_, _) =>
        {
            // DispatcherTimer 会按间隔反复触发，这里只要一次，先停再写。
            timer.Stop();
            Save();
        };
        return timer;
    }

    /// <summary>读盘。文件缺失、结构版本不符、JSON 损坏三种情况都会落到「重建」。</summary>
    // 三种异常的处理差别只在「要不要备份旧文件」：
    //   缺失 → 直接建默认文件（没东西可备份）；
    //   版本不符 → 备份后重建（旧结构有诊断价值，留一份 .bak）；
    //   JSON 损坏 → 备份后重建（同上，而且用户可能手工编辑坏了）。
    private static LocalCacheData LoadFromDisk()
    {
        var path = FilePath;
        try
        {
            if (!File.Exists(path))
            {
                var fresh = new LocalCacheData();
                WriteToDisk(fresh);
                return fresh;
            }

            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<LocalCacheData>(json, Options);

            // 反序列化成功但结构不对（旧版本写的）：整份丢弃。演示项目不做逐版本迁移，
            // 因为缓存里的东西全是可再生的展示数据，重建比写迁移代码划算。
            if (data is null || data.SchemaVersion != CurrentSchemaVersion)
            {
                BackupBrokenFile(path);
                var rebuilt = new LocalCacheData();
                WriteToDisk(rebuilt);
                return rebuilt;
            }

            Normalize(data);
            return data;
        }
        catch (Exception ex)
        {
            // IOException / UnauthorizedAccessException / JsonException / NotSupportedException
            // 统一按「这份文件用不了」处理：备份 → 重建 → 继续启动。
            // 宁可丢掉缓存，也不能让程序因为一个缓存文件起不来。
            LastError = ex.Message;
            BackupBrokenFile(path);
            var fallback = new LocalCacheData();
            WriteToDisk(fallback);
            return fallback;
        }
    }

    /// <summary>反序列化后的兜底：保证所有对象与列表都不是 null，列表里也没有 null 元素。</summary>
    // 手工编辑 JSON 写成 "login": null / "customers": null 是唯一能走到这里的路径，
    // 但少了这一步，调用方第一次点「记住我」或读客户列表就会 NullReferenceException。
    // v2 起 workspace 下面又多了一层嵌套（客户里还有四个列表），逐层兜一遍。
    private static void Normalize(LocalCacheData data)
    {
        data.Login ??= new LoginCache();
        data.Window ??= new WindowCache();
        data.Ai ??= new AiConfigCache();
        data.Workspace ??= new WorkspaceCache();

        var ws = data.Workspace;
        ws.Customers ??= new List<CustomerCache>();
        ws.Tasks ??= new List<TaskCache>();
        ws.Metrics ??= new List<MetricCache>();
        ws.Messages ??= new List<MessageCache>();

        // 列表里的元素可能被手写成 null，过滤掉，避免回放时崩。
        ws.Customers.RemoveAll(c => c is null);
        ws.Tasks.RemoveAll(t => t is null);
        ws.Metrics.RemoveAll(m => m is null);
        ws.Messages.RemoveAll(m => m is null);

        // 客户内部还有四个列表，同样逐层兜底 —— 只写 "name" 不写 "runs" 是最常见的写法。
        foreach (var c in ws.Customers)
        {
            c.Activities ??= new List<ActivityCache>();
            c.Staff ??= new List<StaffCache>();
            c.Runs ??= new List<RunCache>();
            c.Files ??= new List<FileEntryCache>();
            c.Activities.RemoveAll(x => x is null);
            c.Staff.RemoveAll(x => x is null);
            c.Runs.RemoveAll(x => x is null);
            c.Files.RemoveAll(x => x is null);
        }
    }

    /// <summary>原子写：先写 .tmp 再替换，避免写一半断电留下半截 JSON。</summary>
    // File.Move(src, dst, overwrite: true) 在同一分区上是「替换目录项」的原子操作，
    // 所以任何时刻读到的都是完整的旧文件或完整的新文件，不会读到写了一半的内容。
    // 这也解释了为什么不用 File.WriteAllText 直接覆盖：那样在写入中途崩溃会留下截断的 JSON，
    // 虽然我们有「损坏即重建」的兜底，但能避免就别制造。
    private static void WriteToDisk(LocalCacheData data)
    {
        try
        {
            // 目录可能不存在（首次启动 / 用户手工删过 AppData）。
            Directory.CreateDirectory(DirectoryPath);

            var json = JsonSerializer.Serialize(data, Options);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
            LastError = null;
        }
        catch (Exception ex)
        {
            // 写失败只记录不抛：缓存是「锦上添花」，不该阻断任何业务流程。
            // 常见原因：目录只读、文件被别的进程占用、磁盘满。
            LastError = ex.Message;
        }
    }

    /// <summary>把坏掉的文件改名成 .bak（保留现场），失败就忽略。</summary>
    private static void BackupBrokenFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.Move(path, path + ".bak", overwrite: true);
        }
        catch (Exception ex)
        {
            // 备份失败不影响主流程，最多是下次启动再遇到同一个坏文件。
            LastError = ex.Message;
        }
    }
}
