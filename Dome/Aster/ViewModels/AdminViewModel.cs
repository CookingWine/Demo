// ============================================================================
// AdminViewModel.cs —— 工作区（工作台 / 协作与审批 / 客户工作空间 / Workflow / KPI）
// 结构参考 HR Agent 交互原型：侧栏四个入口 + 客户列表 + 客户详情四页签 + 三步核对。
// 页面切换用 AdminPage 枚举 + 一组 bool 供 XAML 绑 IsVisible。
// 本地持久化：工作区的全部展示数据（客户及其详情、待办、KPI、对话）都来自
// Services.LocalCache 的 workspace 段，代码里没有任何硬编码演示数据；
// 启动时 LoadWorkspace 读出来画，发消息 / 审批之后 SaveWorkspace 整体写回。
// AI 对话（v3 起）：工作台的 Agent 对话经 Services.AiChatService 走 OpenAI 兼容协议
// 流式调用真实大模型（DeepSeek 等）；模型可通过 Function Calling 调本文件的五个工具
// 真实操作工作区（查询客户 / 待办、新建客户 / 待办、审批），工具与界面按钮共用同一批
// 数据函数与 SaveWorkspace 落盘链路 —— 不存在「界面上看不到的暗改」。
// ============================================================================
//
// ── 页面状态机（第二轮补充）─────────────────────────────────────────────
// 唯一真值是私有字段 _page（AdminPage 枚举）。IsWorkbench / IsCustomers / IsDetail /
// IsApproval / IsWorkflow / IsKpi 六个 bool 全部由它派生，AdminView 用它们互斥显示六块布局。
// 所有入口最终都收敛到 Go(page)，区别只在「进页面之前要不要先准备数据」：
//
//   入口                                路径                             落点
//   工作台快捷卡                        CardCommand（按卡片 Title 分派）  Go(Kpi / Approval / Customers)
//   二级导航 工作台 / 审批 / 客户 / Workflow  Select(key)                   Go(对应页)
//   二级导航 项目 / 最近会话             Select(key) → OpenCustomer(c, tab)  Go(Detail)
//   客户表 客户名 / 查看 →               OpenCustomerCommand               Go(Detail)
//   详情页 返回客户列表                  BackCommand                       Go(Customers)
//   Workflow 归档                        ArchiveRun                        Go(Customers)
//   未识别的 key（recent-1 之类）        Select 的 default                 Go(Workbench)
//
//   工作台 ──点快捷卡──→ KPI 与考勤 / 协作与审批 / 客户工作空间
//   客户工作空间 ──点列表行──→ 客户详情（base 页签）──点页签──→ 同页的 people / runs / files
//   客户详情 ──点返回──→ 客户工作空间
//   Workflow ──点归档──→ 客户工作空间
//
// 读代码时抓住两条约束：
// 1) 只有 Go 会写 _page；新增页面要在状态机这里补一条入口，别把页面切换逻辑写进 View；
// 2) Go 只负责「换页面 + 发通知」，不碰任何集合；数据由 OpenCustomer / FillDetail / RefreshXxx 准备，
//    因此排查页面空白时先确认切换前那一步有没有跑过。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Aster.Services;
using Avalonia.Media;
using Avalonia.Threading;

namespace Aster.ViewModels;

// 纯状态标记：不参与运算、不持久化、顺序也没有含义（代码只做相等比较）。
// 新增页面时要做三件事：加枚举值、加对应的 IsXxx 属性并在 Go 里补通知、给 TopBarTitle 补一条分支。
/// <summary>工作区内的页面。</summary>
public enum AdminPage
{
    Workbench,
    Customers,
    Detail,
    Approval,
    Workflow,
    Kpi
}

// 两张表必须同步维护：同一个状态在 Foreground 与 Background 里出现的位置要一致，
// 否则会出现「文字绿、底色琥珀」。0.12 是 12% 透明度，把同一种颜色压成浅底，
// 与实色文字叠出「浅底 + 亮字」的标签效果；#9BA3B7 / #FFFFFF 是给未知状态的兜底灰。
/// <summary>状态标签的统一配色：绿=完成、琥珀=待处理、红=退回、灰=其它。</summary>
internal static class StatusPalette
{
    public static IBrush Foreground(string status) => status switch
    {
        "已完成" or "已通过" or "已归档" or "金额一致" or "在册" => Palette.Solid(Palette.GreenColor),
        "待复核" or "待补材料" or "待处理" or "待归档" => Palette.Solid(Palette.AmberColor),
        "已退回" or "金额差异" => Palette.Solid(Palette.RedColor),
        _ => Palette.Solid("#9BA3B7")
    };

    public static IBrush Background(string status) => status switch
    {
        "已完成" or "已通过" or "已归档" or "金额一致" or "在册" => Palette.Alpha(Palette.GreenColor, 0.12),
        "待复核" or "待补材料" or "待处理" or "待归档" => Palette.Alpha(Palette.AmberColor, 0.12),
        "已退回" or "金额差异" => Palette.Alpha(Palette.RedColor, 0.12),
        _ => Palette.Alpha("#FFFFFF", 0.06)
    };
}

// v3 起改为继承 ObservableObject：Agent 的回复要流式逐字追加，Text 必须可变且发通知
// （原 init-only 设计在「整段出现」时代成立，流式时代每收到一段增量就要原位刷新同一条消息）。
// Author / IsUser 仍然 init-only —— 一条消息是谁发的，生下来就定了，没有运行期改变的道理；
// IsError 是唯一的例外：请求先正常出发、半路失败时占位消息要就地转成错误样式。
// IsAgent 是 IsUser 的取反，XAML 靠这一对布尔把同一条消息渲染成「左侧无气泡」或「右侧蓝气泡」。
/// <summary>对话消息。</summary>
public sealed class ChatMessage : ObservableObject
{
    private string _text = string.Empty;
    private bool _isStreaming;
    private bool _isError;

    /// <summary>发送者显示名（"Agent" / "我"）。</summary>
    public string Author { get; init; } = "Agent";

    /// <summary>消息正文。流式期间由 AI 循环原位追加（每次 Set 都会触发界面刷新）。</summary>
    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    /// <summary>true = 用户发的（右侧蓝气泡），false = Agent 发的（左侧无气泡）。</summary>
    public bool IsUser { get; init; }

    public bool IsAgent => !IsUser;

    /// <summary>普通 Agent 气泡是否显示；确认卡片由单独的块渲染，不能同时显示一条空正文。</summary>
    public bool ShowAgentBubble => IsAgent && !IsConfirmCard;

    /// <summary>错误提示消息（作者名与正文走红色）。不持久化、不进入下轮对话上下文。</summary>
    public bool IsError
    {
        get => _isError;
        set
        {
            if (!Set(ref _isError, value)) return;
            // 两个画刷属性都读 _isError，转错误态时要连带刷新，否则颜色不变。
            OnPropertyChanged(nameof(AuthorBrush));
            OnPropertyChanged(nameof(TextBrush));
        }
    }

    /// <summary>工具执行回执（"已执行：新建客户 …"）。持久化，但不回传给模型当对话历史。</summary>
    public bool IsToolNote { get; init; }

    // ---- 写操作确认卡片 ----
    // 它不是普通消息：不落盘、不进上下文，只在界面上临时存在，承载「确认执行 / 取消」两个按钮。
    // 放在 ChatMessage 上而不是单独集合里，是为了复用消息列表的模板与滚动逻辑。

    /// <summary>true = 这是写操作确认卡片（不是对话正文）。</summary>
    public bool IsConfirmCard { get; init; }

    /// <summary>卡片正文：逐条列出「将执行的操作 + 关键参数」。</summary>
    public string ConfirmDetail { get; init; } = string.Empty;

    private bool _isConfirmExpired;
    private string _confirmState = string.Empty;

    /// <summary>卡片是否已结束（已确认 / 已取消 / 已过期）。true 时按钮禁用。</summary>
    public bool IsConfirmExpired
    {
        get => _isConfirmExpired;
        set
        {
            if (!Set(ref _isConfirmExpired, value)) return;
            // 作者行文案读它（「待确认」→「已结束」），必须连带通知。
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    /// <summary>结束原因（已确认 / 已取消 / 已过期），显示在卡片按钮上方。</summary>
    public string ConfirmState
    {
        get => _confirmState;
        set
        {
            if (!Set(ref _confirmState, value)) return;
            OnPropertyChanged(nameof(HasConfirmState));
        }
    }

    /// <summary>是否有结束原因可显示。</summary>
    public bool HasConfirmState => !string.IsNullOrEmpty(_confirmState);

    /// <summary>Agent 回复是否仍在流式生成中。驱动作者行的「正在输入」提示。</summary>
    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            if (!Set(ref _isStreaming, value)) return;
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    /// <summary>作者行显示的文案：确认卡片标注「待确认」、流式期间标注「正在输入」，其余时刻就是 Author。</summary>
    public string DisplayName => IsConfirmCard
        ? (_isConfirmExpired ? $"{Author} · 已结束" : $"{Author} · 待确认")
        : _isStreaming ? $"{Author} · 正在输入…" : Author;

    /// <summary>作者行的颜色：错误红、工具回执更弱的 Text3 灰、普通与原 XAML 样式一致的 Text2。</summary>
    public IBrush AuthorBrush => IsError
        ? Palette.Solid(Palette.RedColor)
        : IsToolNote
            ? Palette.Solid("#434A5C")
            : Palette.Solid("#626A7D");

    /// <summary>正文颜色：错误消息整段红，其余保持正文主色（对应令牌 Text0 #E9ECF4）。</summary>
    public IBrush TextBrush => IsError ? Palette.Solid(Palette.RedColor) : Palette.Solid("#E9ECF4");
}

// Chip 是左上角的小胶囊文案（创建 / 3 项 / 12 条），Tone 决定胶囊配色：
// green / red 各有专用色，其余（默认 accent）走品牌蓝。Command 由构造函数注入同一个 CardCommand，
// 卡片自身不携带目标页面 —— 点哪张卡、去哪个页面是命令里按 Title 分派的。
/// <summary>工作台顶部快捷卡。</summary>
public sealed class WorkbenchCard
{
    public string Chip { get; init; } = string.Empty;
    public string Tone { get; init; } = "accent";
    public string Title { get; init; } = string.Empty;
    public string Desc { get; init; } = string.Empty;

    /// <summary>点击后去哪个入口：customer-new / kpi / approval。</summary>
    // 用稳定标识而不是 Title 当分派键：改文案（「新建项目」→「新建客户」）不会再静默改掉点击行为。
    public string Target { get; init; } = string.Empty;

    public ICommand Command { get; init; } = null!;

    public IBrush ChipBackground => Tone switch
    {
        "green" => Palette.Alpha(Palette.GreenColor, 0.14),
        "red" => Palette.Alpha(Palette.RedColor, 0.14),
        _ => Palette.Alpha(Palette.AccentColor, 0.16)
    };

    public IBrush ChipForeground => Tone switch
    {
        "green" => Palette.Solid(Palette.GreenColor),
        "red" => Palette.Solid(Palette.RedColor),
        _ => Palette.Solid("#9FB4FF")
    };
}

// 只读演示数据（init-only）。派生属性都在 XAML 里直接绑：
// PendingText 与 HasPending 配合，同一列放两个 TextBlock，一个显示「3 项」、一个显示破折号，
// 靠 IsVisible 和 {Binding !HasPending} 二选一，省掉值转换器。
// OpenCommand 由 LoadWorkspace 注入 OpenCustomerCommand，XAML 再用 CommandParameter="{Binding}"
// 把「这一行」当参数传回命令，命令体里就能拿到被点的客户。
/// <summary>客户档案。</summary>
public sealed class CustomerItem : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Short { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Owner { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string Contact { get; init; } = string.Empty;
    public string Service { get; init; } = string.Empty;
    public string Tax { get; init; } = string.Empty;
    public string File { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public int People { get; init; }
    public int Pending { get; init; }

    // 详情页四个页签的行数据，跟着客户一起从本地缓存读出来（v2 起不再由代码现造）。
    // 用 init-only 的只读列表而不是 ObservableCollection：这份数据在客户对象上是不变的，
    // 界面显示的是 FillDetail 复制到 PeopleRows / RunRows / FileRows / Activities 的那一份。
    public IReadOnlyList<ActivityItem> Activities { get; init; } = Array.Empty<ActivityItem>();
    public IReadOnlyList<PersonRow> Staff { get; init; } = Array.Empty<PersonRow>();
    public IReadOnlyList<RunRow> Runs { get; init; } = Array.Empty<RunRow>();
    public IReadOnlyList<FileRow> Files { get; init; } = Array.Empty<FileRow>();

    public ICommand? OpenCommand { get; init; }

    public bool HasPending => Pending > 0;
    public string PendingText => Pending > 0 ? $"{Pending} 项" : "—";
    public string PeopleText => $"{People} 人";
    public string Meta => $"{Code} · {Type} · 负责人：{Owner}";

    public IBrush StateBrush => StatusPalette.Foreground(State);
    public IBrush StateBackground => StatusPalette.Background(State);
}

// 七个字段里只有 Status 是可变状态，其余是演示数据的固定文案。
// Note 是普通自动属性（没有 Set、不发通知），它能在界面上出现，靠的是 Decide 之后
// RefreshTasks 触发集合重建、容器重新绑定；若哪天改成只改数据不重建，Note 就不会刷新。
/// <summary>审批 / 待办事项。</summary>
public sealed class TaskItem : ObservableObject
{
    private string _status = "待处理";

    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Kind { get; init; } = "审批";
    public string CustomerName { get; init; } = string.Empty;
    public string Due { get; init; } = "今日";
    public string Note { get; set; } = string.Empty;

    public string Status
    {
        get => _status;
        set
        {
            // Set 只发 Status 自己这一条；下面三个都是读 _status 的派生属性，必须手动补发。
            // 漏掉它们会出现「状态文字变了、标签颜色没变、已处理项还留着审批按钮」。
            if (!Set(ref _status, value)) return;
            OnPropertyChanged(nameof(IsPending));
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(StatusBackground));
        }
    }

    public bool IsPending => _status == "待处理";
    public IBrush StatusBrush => StatusPalette.Foreground(_status);
    public IBrush StatusBackground => StatusPalette.Background(_status);
    public bool HasNote => !string.IsNullOrEmpty(Note);

    public ICommand? ApproveCommand { get; init; }
    public ICommand? RejectCommand { get; init; }
}

/// <summary>详情页「最近更新」条目。</summary>
public sealed class ActivityItem
{
    public string Text { get; init; } = string.Empty;
    public string Time { get; init; } = string.Empty;
}

/// <summary>人员记录行。</summary>
public sealed class PersonRow
{
    public string No { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string IdInfo { get; init; } = "已脱敏";
    public string Customer { get; init; } = string.Empty;
    public string Status { get; init; } = "在册";
    public string Source { get; init; } = string.Empty;
    public IBrush StatusBrush => StatusPalette.Foreground(Status);
    public IBrush StatusBackground => StatusPalette.Background(Status);
}

/// <summary>核对记录行。</summary>
public sealed class RunRow
{
    public string Batch { get; init; } = string.Empty;
    public string Period { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Pending { get; init; } = "—";
    public string Date { get; init; } = string.Empty;
    public IBrush StatusBrush => StatusPalette.Foreground(Status);
    public IBrush StatusBackground => StatusPalette.Background(Status);
}

// Tone 只有两个取值：ok（Excel 产出，标签走绿）与默认 muted（原始材料，标签走灰）。
// 这里借 StatusPalette 的语义色，把 "ok" 翻译成「已完成」再去查表，避免再写一套配色。
/// <summary>文件与产出行。</summary>
public sealed class FileRow
{
    public string Name { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public string Related { get; init; } = string.Empty;
    public string Time { get; init; } = string.Empty;
    public string Tone { get; init; } = "muted";

    public IBrush PurposeBrush => StatusPalette.Foreground(Tone == "ok" ? "已完成" : "其它");
    public IBrush PurposeBackground => StatusPalette.Background(Tone == "ok" ? "已完成" : "其它");
}

// Business / System 是两侧税额，Diff 与 DiffText 都是算出来的，不存储：
// Math.Round(..., 2) 先收敛到分，避免 0.1 + 0.2 这类二进制误差把「一致」判成「有差异」；
// 0.005 以下会被舍成 0.00，因此「差 4 厘」不会被标成红字。
// DiffText 用全角加号「＋」和减号「−」而非 ASCII，是为了和表格里的中文字重、字宽对齐。
/// <summary>核对结果行。</summary>
public sealed class ResultRow
{
    public string Person { get; init; } = string.Empty;
    public double Business { get; init; }
    public double System { get; init; }
    public string Result { get; init; } = "金额一致";
    public string Status { get; init; } = "本项通过";

    public double Diff => Math.Round(Business - System, 2);
    public string DiffText => Diff == 0 ? "0.00" : (Diff > 0 ? "＋" : "−") + Math.Abs(Diff).ToString("0.00");
    public bool HasDiff => Diff != 0;
    public IBrush DiffBrush => HasDiff ? Palette.Solid(Palette.RedColor) : Palette.Solid("#9BA3B7");
    public IBrush StatusBrush => StatusPalette.Foreground(Status);
    public IBrush StatusBackground => StatusPalette.Background(Status);
}

/// <summary>KPI 指标行。</summary>
public sealed class MetricRow
{
    public string Title { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
}

// 字段分三层：
// 1) 页面与筛选状态（_page / _detailTab / _query / _filter / _taskFilter / _draft），
//    改完必须给对应的派生属性补通知，界面绑的都是派生属性而不是字段；
// 2) Workflow 向导状态（_wfStep / _wfSkill / _wfPeriod / _wfNote / _wfAccepted / _wfError）；
// 3) _allCustomers / _allTasks 两个源列表：过滤只重建对外的 ObservableCollection，
//    源列表始终保持全量 —— 但它们本身不再有硬编码种子，内容全部来自本地缓存
//    （Services.LocalCache 的 workspace 段，见 LoadWorkspace / SaveWorkspace）。
public sealed class AdminViewModel : ObservableObject
{
    /// <summary>新建待办时「不关联客户」的占位项。ComboBox 不能绑 null 项，用一个固定字符串占位。</summary>
    private const string NoCustomerOption = "（不关联客户）";

    private AdminPage _page = AdminPage.Workbench;
    private CustomerItem? _current;
    private string _detailTab = "base";
    private string _query = string.Empty;
    private string _filter = "全部客户";
    private string _taskFilter = "待处理";
    private string _draft = string.Empty;
    private int _wfStep;
    private string _wfSkill = "个税";
    private string _wfPeriod = "2026-08";
    private string _wfNote = string.Empty;
    private bool _wfAccepted;
    private string _wfError = string.Empty;

    // 新建表单状态（客户 / 待办 / 指标三套）。
    // 表单字段一律存 string 而不是 int / 枚举：TextBox 与 ComboBox 绑的都是 string，
    // 转换集中到 SaveXxx 里做一次，非法输入才能被拦下来给提示，而不是在绑定层静默变成 0。
    private bool _isCustomerFormOpen;
    private bool _isTaskFormOpen;
    // 编辑态：null = 新建，非 null = 正在改这个 Id 的客户。
    // 用「编辑目标 Id」而不是「复制一份草稿」，是因为保存时要按 Id 找回原位置、
    // 并且必须保留详情四张表（人员 / 核对 / 文件 / 最近更新）—— 表单只改基础字段。
    private string? _editingCustomerId;
    private bool _isDeleteConfirmOpen;
    private string _deleteTargetId = string.Empty;
    private string _deleteTargetName = string.Empty;
    private string _formName = string.Empty;
    private string _formShort = string.Empty;
    private string _formCode = string.Empty;
    private string _formType = "劳务派遣";
    // 负责人默认留空：以前预填「张客服」会让人以为系统里真有一个叫这名字的登录用户。
    private string _formOwner = string.Empty;
    private string _formCity = string.Empty;
    private string _formContact = string.Empty;
    private string _formService = string.Empty;
    private string _formTax = "已登记";
    private string _formPeople = "0";
    private string _formPending = "0";
    private string _formState = "待复核";
    private string _formError = string.Empty;
    private string _taskFormTitle = string.Empty;
    private string _taskFormKind = "审批";
    private string _taskFormCustomer = NoCustomerOption;
    private string _taskFormDue = "今日";
    private string _taskFormNote = string.Empty;
    private string _taskFormError = string.Empty;
    private string _metricFormTitle = string.Empty;
    private string _metricFormValue = string.Empty;
    private string _metricFormNote = string.Empty;
    private string _metricFormError = string.Empty;

    // ---- AI 对话状态（v3）----
    // _isAiBusy 是防重入的唯一真值：一轮对话（可能含多次工具调用轮）从发出请求到收完
    // 最终文本都算「忙」，期间 Send 直接早退、发送按钮由 CanExecute 置灰。
    // _streamBuffer / _streamTarget / _streamStarted 三个字段只允许在 UI 线程触碰：
    // onDelta 回调发生在后台读流线程，必须经 Dispatcher.UIThread.Post 转回 UI 线程再动它们。
    private readonly AiChatService _ai = new();
    private bool _isAiBusy;
    private CancellationTokenSource? _aiCts;
    private readonly StringBuilder _streamBuffer = new();
    private DispatcherTimer? _streamFlushTimer;
    private ChatMessage? _streamTarget;
    private bool _streamStarted;

    // 会话级模型选择：null = 使用设置页的全局默认（LocalCache.Data.Ai.Model）。
    // 只活在内存里，不写缓存 —— 重启后回落全局默认，这是「仅当前会话生效」的唯一真值。
    private string? _sessionModel;

    /// <summary>工具调用循环的轮数上限：防模型反复要求执行工具把对话拖成死循环。</summary>
    private const int MaxToolRounds = 8;

    private readonly List<CustomerItem> _allCustomers = new();
    private readonly List<TaskItem> _allTasks = new();

    // 构造顺序是有意排的，改动顺序容易踩两个坑：「界面先绑到空集合」和「首屏停在详情页」。
    // 步骤 1：先建全部命令（共 14 个）。下面的卡片与演示数据会直接引用其中几个
    //        （Cards[i].Command、TaskItem.ApproveCommand），命令必须早于这些对象存在，否则绑上去是 null。
    // 步骤 2：建 Cards 与 Messages。Cards 的每一项都指向同一个 CardCommand —— 具体去哪个页面
    //        由命令体按卡片 Title 判断，卡片本身只是展示数据。
    // 步骤 3：LoadWorkspace 从本地缓存读出工作区的全部数据（客户及其详情 / 待办 / KPI / 对话），
    //        代码里不再有任何硬编码演示数据 —— 缓存为空，界面就是空的。
    // 步骤 4：建对外集合。Customers 从源列表整体拷贝；People / Run / File / Activities 先空着，
    //        等 OpenCustomer 填充；Tasks 也先空，交给下一步筛选。
    // 步骤 5：RefreshTasks 按默认筛选「待处理」灌入任务，同时发出 TaskSummary / PendingCount。
    // 步骤 6：OpenCustomer(第一个客户) 先把详情数据准备好。它内部会 Go(Detail)。
    // 步骤 7：Go(Workbench) 把首屏拨回工作台，用户看不到步骤 6 的中间跳转。
    //        这一步也让六个 IsXxx 第一次算出结果，XAML 由此决定渲染哪块布局。
    public AdminViewModel()
    {
        OpenCustomerCommand = new RelayCommand(o =>
        {
            if (o is CustomerItem c) OpenCustomer(c, "base");
        });
        // 快捷卡命令：三张卡共用一个命令，靠 Target 标识分派。
        // customer-new 与二级导航的「＋ 新建客户」走同一条路径：先切到客户页，再展开新建表单。
        CardCommand = new RelayCommand(o =>
        {
            if (o is not WorkbenchCard card) return;
            switch (card.Target)
            {
                case "kpi": Go(AdminPage.Kpi); break;
                case "approval": Go(AdminPage.Approval); break;
                case "customer-new": Go(AdminPage.Customers); OpenCustomerForm(); break;
                default: Go(AdminPage.Customers); break;
            }
        });
        BackCommand = new RelayCommand(() => Go(AdminPage.Customers));
        TabCommand = new RelayCommand(o =>
        {
            if (o is string tab) SelectTab(tab);
        });
        TaskFilterCommand = new RelayCommand(o =>
        {
            TaskFilter = o as string ?? "待处理";
            RefreshTasks();
        });
        // 发送命令带 CanExecute：AI 忙碌期间按钮置灰。注意 RelayCommand 没有自动重查机制，
        // IsAiBusy 变化时必须手动 RaiseCanExecuteChanged（见 IsAiBusy 的 setter）。
        SendCommand = new RelayCommand(Send, () => !_isAiBusy);
        // 确认卡片的两个动作。CanExecute 不参与判断：卡片结束后由 IsConfirmExpired 把按钮置灰，
        // 命令内部还会再校验一次状态，避免计时器与点击之间的竞态。
        ConfirmPendingCommand = new RelayCommand(ConfirmPending);
        CancelPendingCommand = new RelayCommand(CancelPending);
        GoWorkflowCommand = new RelayCommand(() => Go(AdminPage.Workflow));
        // 审批命令：参数是 XAML 用 CommandParameter 传回的 TaskItem，两条只差目标状态字符串，
        // 具体改状态与刷新列表都交给 Decide。
        ApproveCommand = new RelayCommand(o =>
        {
            if (o is TaskItem t) Decide(t, "已通过");
        });
        RejectCommand = new RelayCommand(o =>
        {
            if (o is TaskItem t) Decide(t, "已退回");
        });
        // 新建类命令：三个「打开表单 → 填 → 保存」的入口。
        // 打开与取消只动表单自己的字段，保存才碰集合与磁盘（见 SaveCustomer / SaveTask / AddMetric）。
        OpenCustomerFormCommand = new RelayCommand(OpenCustomerForm);
        CancelCustomerFormCommand = new RelayCommand(CloseCustomerForm);
        SaveCustomerCommand = new RelayCommand(SaveCustomer);
        // 编辑 / 删除都从客户详情页触发，参数是当前客户（XAML 传 CommandParameter="{Binding Current}"）。
        // 参数为空时退回 _current：详情页永远有一个 Current，这里只是多一层兜底。
        EditCustomerCommand = new RelayCommand(o =>
        {
            var target = o as CustomerItem ?? _current;
            if (target is not null) OpenEditCustomerForm(target);
        });
        DeleteCustomerCommand = new RelayCommand(o =>
        {
            var target = o as CustomerItem ?? _current;
            if (target is not null) AskDeleteCustomer(target);
        });
        ConfirmDeleteCommand = new RelayCommand(ConfirmDelete);
        CancelDeleteCommand = new RelayCommand(CloseDeleteConfirm);
        OpenTaskFormCommand = new RelayCommand(OpenTaskForm);
        CancelTaskFormCommand = new RelayCommand(CloseTaskForm);
        SaveTaskCommand = new RelayCommand(SaveTask);
        AddMetricCommand = new RelayCommand(AddMetric);
        WfStartCommand = new RelayCommand(StartRun);
        WfResolveCommand = new RelayCommand(ResolveRun);
        WfArchiveCommand = new RelayCommand(ArchiveRun);
        // 重新开始：把向导状态整体复位，但不改 _page，用户仍停在第 0 步的「确认范围与材料」。
        // WfAccepted 也要清：免责勾选跟着这一轮走，下次必须重新确认。
        WfRestartCommand = new RelayCommand(() =>
        {
            WfStep = 0;
            WfAccepted = false;
            WfNote = string.Empty;
            WfError = string.Empty;
            WfRows.Clear();
        });
        SelectCommand = new RelayCommand(o =>
        {
            if (o is string key) Select(key);
        });

        Cards = new ObservableCollection<WorkbenchCard>
        {
            // Chip 改成了中性文案（原来写死「3 项」「12 条」）：v2 起待办数量由文件决定，
            // 卡片上再挂一个硬编码数字就会和实际列表对不上。
            // 术语统一为「客户」：仓库里没有独立的「项目」实体，之前叫「新建项目」会让人以为还有一套项目模型。
            new() { Chip = "创建", Tone = "accent", Target = "customer-new", Title = "新建客户", Desc = "创建客户档案，数据只写入本机演示缓存。", Command = CardCommand },
            new() { Chip = "查看", Tone = "green", Target = "kpi", Title = "KPI 考勤", Desc = "查看演示指标与考勤数据。", Command = CardCommand },
            new() { Chip = "查看", Tone = "red", Target = "approval", Title = "待办事项", Desc = "演示审批流程，结果只更新本地状态。", Command = CardCommand }
        };

        // 对话从空集合起步：缓存里有几条就画几条，没有再冷启动问候语 ——
        // 「界面内容全部来自本地文件」这条原则不给自己留例外。
        Messages = new ObservableCollection<ChatMessage>();
        // 工作台会话区与左侧「最近会话」都靠 HasMessages 判空。
        // 这里只补属性通知，不重建导航：导航重建统一由 WorkspaceChanged 负责，
        // 否则一轮对话里每次 Add 都会让壳层重排一次侧栏。
        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMessages));

        // 步骤 3：从本地缓存读出工作区全部数据（客户及其详情、待办、KPI、对话）。
        // 这里是整条链上唯一读文件的地方；缓存为空时四个集合就是空的，属于合法状态。
        LoadWorkspace();

        Customers = new ObservableCollection<CustomerItem>(_allCustomers);
        Tasks = new ObservableCollection<TaskItem>();
        PeopleRows = new ObservableCollection<PersonRow>();
        RunRows = new ObservableCollection<RunRow>();
        FileRows = new ObservableCollection<FileRow>();
        Activities = new ObservableCollection<ActivityItem>();

        RefreshTasks();
        // 待办表单的客户下拉要跟着已有客户走：构造函数里建一次，新建客户之后再刷一次。
        RefreshCustomerOptions();
        // 模型下拉：预设 ∪ 设置页自定义模型，构造时合并一次即可。
        RefreshModelOptions();
        // 没有任何客户时不能直接下标取第一个（会抛 IndexOutOfRange）：
        // 退化成「停在客户工作空间页」并显示空态提示，详情页自然也就进不去。
        if (_allCustomers.Count > 0) OpenCustomer(_allCustomers[0], "base");
        Go(AdminPage.Workbench);
    }

    // ===================== 页面状态 =====================
    public bool IsWorkbench => _page == AdminPage.Workbench;
    public bool IsCustomers => _page == AdminPage.Customers;
    public bool IsDetail => _page == AdminPage.Detail;
    public bool IsApproval => _page == AdminPage.Approval;
    public bool IsWorkflow => _page == AdminPage.Workflow;
    public bool IsKpi => _page == AdminPage.Kpi;

    public string TopBarTitle => _page switch
    {
        AdminPage.Approval => "协作与审批",
        AdminPage.Customers or AdminPage.Detail => "客户工作空间",
        AdminPage.Workflow => "Workflow",
        AdminPage.Kpi => "KPI 与考勤",
        _ => "工作台"
    };

    // 全类唯一的页面写入口。调用方：构造函数、CardCommand、BackCommand、Select、OpenCustomer、
    // ArchiveRun。前提：调用前目标页要用的集合已准备好（OpenCustomer 就是先填数据再进来）。
    // 做完之后：_page 变化 → 六条 IsXxx 加 TopBarTitle 共七条通知 → XAML 里六块布局重算可见性、
    // 顶栏标题重读。这里不碰任何集合，所以一条集合通知都不需要发。
    private void Go(AdminPage page)
    {
        // 步骤 1：改唯一真值。字段私有，类外没有入口，类内也只有本方法写它。
        _page = page;
        // 步骤 2：六个派生 bool 逐个通知。Set 在这里用不上（没有同名属性可 Set），
        // 而绑定只认属性名：漏发哪一个，那块布局就会卡在上一页的可见性上。
        OnPropertyChanged(nameof(IsWorkbench));
        OnPropertyChanged(nameof(IsCustomers));
        OnPropertyChanged(nameof(IsDetail));
        OnPropertyChanged(nameof(IsApproval));
        OnPropertyChanged(nameof(IsWorkflow));
        OnPropertyChanged(nameof(IsKpi));
        // 步骤 3：顶栏标题同样是 switch 算出来的计算属性，不补发就停在上一页的文案。
        OnPropertyChanged(nameof(TopBarTitle));
    }

    // 调用方只有一个：MainWindowViewModel.SelectSubNav —— 用户点二级导航的任意条目，
    // 壳层先同步高亮，再把 entry.Key 原样传进来；壳并不关心这些 key 对应哪个页面。
    // 前提：构造函数已跑完（依赖 _allCustomers 至少有一条）。重复调用安全，最终状态只由 key 决定。
    // 做完之后：_page 落到目标页并发通知，XAML 切换布局。
    //
    // key 的四种语义：
    // 1) 页面 key：approval / clients / customers / workflow / kpi，直接 Go。
    //    clients 与 customers 是同一个页面的别名（rail 用 clients，内部习惯用 customers）。
    // 2) 客户 key：customer:{Id} —— 二级导航「客户」区块按真实缓存数据生成，点谁开谁。
    //    customer-new 是「＋ 新建客户」入口：切到客户页并当场展开新建表单。
    // 3) 旧详情 key：customer-qinghe / detail / detail-people / detail-runs / detail-files，
    //    都是「打开第一个客户 + 指定页签」。保留是为了兼容旧调用点，新代码不再生成这些 key。
    // 4) workbench 以及任何拼错的 key 落到 default → 工作台；空工作区里点「最近会话」也走这里。
    //
    // 为什么不用字典：分支只有十来个，且详情分支要传不同的 tab 字符串，字典的值就得是 lambda，
    // 可读性反而不如一个 switch。
    /// <summary>外壳（一级导航 / 二级会话列表）调用，把侧栏的 key 映射到页面。</summary>
    public void Select(string key)
    {
        // 详情类 key 都要落到「某个客户」上，而客户来自本地缓存，可能一个都没有。
        // 这里统一前置判断：没有客户时直接去客户工作空间，由那里的空态提示告诉用户去补数据，
        // 而不是让它去访问 _allCustomers[0] 抛异常。
        var hasFirst = _allCustomers.Count > 0;

        switch (key)
        {
            // 协作与审批：只换页，任务列表由 RefreshTasks 提前准备好，这里不重建。
            case "approval": Go(AdminPage.Approval); break;
            // 客户工作空间的两个别名，落到同一页；保留 customers 是为了兼容内部旧调用。
            case "clients":
            case "customers": Go(AdminPage.Customers); break;
            // Workflow 向导：不重置 WfStep，所以上次停在第几步这次还停在第几步。
            case "workflow": Go(AdminPage.Workflow); break;
            // KPI：纯只读页，数据在构造函数 LoadWorkspace 里一次性建好。
            case "kpi": Go(AdminPage.Kpi); break;
            // 二级导航「客户」区块：key 里带 Id，按 Id 精确打开对应客户。
            case string k when k.StartsWith("customer:", StringComparison.Ordinal):
                OpenCustomerById(k["customer:".Length..]);
                break;
            // 「＋ 新建客户」：先切到客户页，再打开新建浮层。
            case "customer-new": Go(AdminPage.Customers); OpenCustomerForm(); break;
            // 以下五个旧 key 是兼容分支，统一落到「第一个客户」，新代码不再生成。
            case "customer-qinghe": OpenFirstCustomer("base", hasFirst); break;
            case "thread-tax": OpenFirstCustomer("runs", hasFirst); break;
            case "detail": OpenFirstCustomer("base", hasFirst); break;
            case "detail-people": OpenFirstCustomer("people", hasFirst); break;
            case "detail-runs": OpenFirstCustomer("runs", hasFirst); break;
            case "detail-files": OpenFirstCustomer("files", hasFirst); break;
            // 兜底：workbench 及任何拼错的 key 都回工作台。
            // 兜底而不抛异常，是为了让壳层新增占位条目时不会把演示点崩。
            default: Go(AdminPage.Workbench); break;
        }
    }

    /// <summary>按 Id 打开客户详情；找不到（例如刚被删除）就退回客户列表，不抛异常。</summary>
    // 调用方：Select 的 customer:{Id} 分支。tab 固定 base：侧栏条目只承诺「打开这位客户」。
    public void OpenCustomerById(string id, string tab = "base")
    {
        var target = _allCustomers.Find(c => c.Id == id);
        if (target is null)
        {
            Go(AdminPage.Customers);
            return;
        }
        OpenCustomer(target, tab);
    }

    /// <summary>打开第一个客户并落到指定页签；没有客户时退到客户工作空间页。</summary>
    // 抽出来只是为了把「空判断」收在一处：上面六个详情 key 各写一遍 if 会很难读。
    private void OpenFirstCustomer(string tab, bool hasFirst)
    {
        if (!hasFirst)
        {
            Go(AdminPage.Customers);
            return;
        }
        OpenCustomer(_allCustomers[0], tab);
    }

    // ===================== 工作台 =====================
    public ObservableCollection<WorkbenchCard> Cards { get; }
    public ObservableCollection<ChatMessage> Messages { get; }
    public ICommand CardCommand { get; }

    /// <summary>确认卡片上的「确认执行」：把暂存的写操作真正落盘，然后继续对话循环。</summary>
    public ICommand ConfirmPendingCommand { get; }

    /// <summary>确认卡片上的「取消」：不执行任何写操作，只回一条说明并写审计。</summary>
    public ICommand CancelPendingCommand { get; }

    // ---- 壳层（左侧二级导航）需要的只读视图 ----
    // 壳层不直接碰 LocalCache，也不复制一份客户列表：它只读这里暴露的全量源数据 + 变化事件。
    // 注意不能用 Customers（那是筛选后的结果），否则搜索框一敲，左侧客户列表就会跟着少几条。

    /// <summary>全量客户（不受搜索 / 筛选影响）。</summary>
    public IReadOnlyList<CustomerItem> AllCustomers => _allCustomers;

    /// <summary>本地缓存里有没有客户。左侧「客户」区块与工作台空态都用它判空。</summary>
    public bool HasAnyCustomer => _allCustomers.Count > 0;

    /// <summary>对话记录里有没有消息。工作台会话区与左侧「最近会话」用它判空。</summary>
    public bool HasMessages => Messages.Count > 0;

    /// <summary>
    /// 工作区结构发生变化：客户增删改、待办变化、指标变化、对话落盘之后触发。
    /// 壳层订阅它重建左侧二级导航（客户条目 / 最近会话 / 空态）。
    /// </summary>
    // 只在 SaveWorkspace 末尾触发，也就是所有写入路径的唯一收口；
    // 流式增量只改 ChatMessage.Text、不落盘，因此不会造成高频重建。
    public event EventHandler? WorkspaceChanged;

    // ---- 当前操作者 ----
    // 壳层在构造与登录成功时注入登录账号；Agent 的系统提示与新建客户的默认负责人都读它。
    // 以前这两处写死「张客服」，登录账号却是 admin —— 演示时一眼假，也会把假人名写进缓存。
    private string _operatorName = "演示账号";

    /// <summary>当前操作者显示名；未注入或注入空白时为「演示账号」。</summary>
    public string OperatorName => _operatorName;

    /// <summary>由壳层注入当前操作者（登录账号）。空白回落「演示账号」。</summary>
    public void SetOperatorName(string? name)
    {
        _operatorName = string.IsNullOrWhiteSpace(name) ? "演示账号" : name.Trim();
    }

    // 声明成 RelayCommand 而不是 ICommand：IsAiBusy 变化时要调 RaiseCanExecuteChanged()
    // 刷新发送按钮的可用态，那是具体类型才有的成员（见 ObservableObject.cs 的说明）。
    public RelayCommand SendCommand { get; }

    /// <summary>AI 是否正在处理一轮对话（请求中或工具循环中）。true 时发送被禁用。</summary>
    public bool IsAiBusy
    {
        get => _isAiBusy;
        private set
        {
            if (!Set(ref _isAiBusy, value)) return;
            // CanExecute 读的是 _isAiBusy，状态变了必须手动让按钮重新求值；
            // 事件没人订阅时这是空操作，没有副作用。
            SendCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// 流式刷新事件：缓冲合并后更新了消息正文时触发。
    /// AdminView 订阅它把对话区滚到底 —— 集合没有新增元素，ItemsControl 的
    /// CollectionChanged 不会响，滚动必须由这里显式通知。
    /// </summary>
    public event EventHandler? ChatContentAppended;

    // ---- 会话级模型选择（工作台输入框工具行的下拉） ----

    /// <summary>内置预设模型清单：与设置页的自定义模型合并去重后进下拉，顺序即下拉顺序。</summary>
    // 都是 OpenAI 兼容服务上真实存在的模型名（DeepSeek / 通义 / Kimi 各取常用款）。
    // 已知边界：切到别的服务商时，清单里只有设置里填的那个能命中，其余选项会得到
    // HTTP 4xx 并以错误消息呈现 —— 这是「预设清单离线可用」的代价，可接受。
    public static readonly string[] PresetModels =
    {
        "deepseek-chat",
        "deepseek-reasoner",
        "qwen-plus",
        "kimi-k2"
    };

    /// <summary>下拉数据源：预设 ∪ 设置页自定义模型（OrdinalIgnoreCase 去重，构造时填一次）。</summary>
    public ObservableCollection<string> ModelOptions { get; } = new();

    /// <summary>当前会话生效的模型名：会话选择优先，否则回落设置页的全局默认。</summary>
    // getter 的回落链：会话字段 → 设置页全局默认 → 内置预设首项（理论上走不到，Ai 段
    // 经 Normalize 后 Model 必非空，这里只防「手工把文件改成空串」的极端情况）。
    // setter 供 ComboBox 的 SelectedItem TwoWay 绑定回写（与 Filter/TaskFilter setter 同款模式）：
    // 只写内存会话字段、不碰 LocalCache —— 「仅当前会话生效」就落在这一行赋值上。
    // 空值与重复值直接忽略：下拉里选回当前项时 Set 返回 false，连通知都不发。
    public string SelectedModel
    {
        get => _sessionModel
            ?? LocalCache.Data.Ai?.Model
            ?? PresetModels[0];
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            _sessionModel = value.Trim();
            OnPropertyChanged();
        }
    }

    /// <summary>把预设清单与设置页的自定义模型合并去重，灌入 ModelOptions（构造时调用一次）。</summary>
    // 设置页改了默认模型后清单不重建、也无需重建：SelectedModel 的回落值读的是
    // 常驻内存的 LocalCache.Data.Ai，下一次发送自然跟随；只有当自定义模型名不在预设里
    // 且程序重启后才会在清单里出现（届时构造函数重跑一遍合并）。
    private void RefreshModelOptions()
    {
        foreach (var m in PresetModels)
            ModelOptions.Add(m);

        var configured = LocalCache.Data.Ai?.Model;
        if (string.IsNullOrWhiteSpace(configured)) return;
        // 去重用 OrdinalIgnoreCase：模型名不区分大小写（deepseek-Chat 与 deepseek-chat 等价）。
        if (!ModelOptions.Contains(configured.Trim(), StringComparer.OrdinalIgnoreCase))
            ModelOptions.Add(configured.Trim());
    }

    public string Draft
    {
        get => _draft;
        // 与 Composer 的 TextBox 双向绑定。code-behind 的 Enter 处理只负责触发 SendCommand，
        // 不直接读 TextBox，所以草稿永远以这个属性为准。Set 在值相等时提前返回，
        // 避免每敲一个相同的字符都发一次通知。
        set => Set(ref _draft, value);
    }

    // 调用方：输入卡右侧的发送按钮（SendCommand）与 code-behind 的 OnComposerKeyDown（Enter）。
    // 前提：不要求当前页面是工作台；AI 忙碌时直接早退（按钮已由 CanExecute 禁用，
    // Enter 路径绕不过 CanExecute，所以方法内还要再拦一道）。
    // 做完之后：Messages 末尾多一条用户消息；已配置 AI 时再多一条流式生长的 Agent 消息，
    // 由 RunAgentLoopAsync 在后台推进到最终回复。
    private void Send()
    {
        // 步骤 1：取快照并 Trim。空串/纯空白直接 return：既不发消息也不清空草稿，
        // 因为用户可能只是误触 Enter，清空会让他丢掉正在写的半句话。
        var text = Draft?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        // 步骤 1.5：上一轮还挂着未确认的写操作时，发新指令视为放弃它 ——
        // 否则卡片会一直悬在对话里，用户以为已经执行（或以为还能执行）。
        AbandonPendingIfAny();
        // 步骤 2：防重入。按钮的 CanExecute 已经拦了一层，这里兜住 Enter 键路径；
        // 忙碌时连草稿都不清，让用户的话留在输入框里等下一轮。
        if (_isAiBusy) return;

        // 步骤 3：先把对话历史快照出来（此刻还没加这条新消息），再把用户消息单独追加进请求。
        // 顺序有意如此：历史里不该包含「正在发送的这条」，否则模型会收到两遍。
        var history = BuildAiHistory();
        history.Add(new AiChatMessage { Role = "user", Content = text });

        // 步骤 4：追加用户消息并清空草稿。集合 Add 会发 CollectionChanged，消息列表自动刷新，
        // code-behind 也订阅了同一事件用来把 ChatScroll 滚到底。
        // 作者用「我」而不是写死的人名：右侧蓝色气泡本身已经表明是当前用户，
        // 写死「张客服」会与登录账号（admin）对不上，属于误导性默认内容。
        Messages.Add(new ChatMessage { Author = "我", Text = text, IsUser = true });
        Draft = string.Empty;
        // 用户的消息是「不可再生的数据」，先落一次盘；Agent 的回复生成完再补一次，
        // LocalCache.SaveSoon 的 700ms 防抖会把这几次合并成真正的磁盘写。
        SaveWorkspace();

        // 步骤 5：未配置 API Key 时不发请求，给一条可操作的引导。
        // 错误消息既不落盘也不进上下文（SaveWorkspace / BuildAiHistory 都会跳过 IsError）。
        var cfg = LocalCache.Data.Ai;
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.ApiKey))
        {
            Messages.Add(new ChatMessage
            {
                Author = "Agent",
                IsError = true,
                Text = "尚未配置 AI 模型：请打开「设置 → AI 模型」填写 API Key（支持 DeepSeek 等 OpenAI 兼容服务），保存后回来再发一次。"
            });
            return;
        }

        // 步骤 6：进入忙态 → 建占位消息（「正在输入…」）→ 起后台对话循环。
        // 具体动作抽到 StartAgentRun，确认后续跑也复用它。
        StartAgentRun(history, 0);
    }

    // ===================== AI 对话循环 =====================
    // 主循环：一轮「流式请求 →（可选）执行工具 → 带结果再请求」，直到模型给出不带
    // tool_calls 的最终回复，或达到 MaxToolRounds 上限。所有 await 都会回到 UI 线程
    // （Avalonia 安装了同步上下文），所以集合与 VM 状态的读写是线程安全的；
    // 唯独 onDelta 回调发生在后台读流线程，它只做 Dispatcher.Post 转发。
    // startRound：确认续跑时传入已消耗的轮数，避免「确认一次就重置 8 轮上限」。
    private async Task RunAgentLoopAsync(List<AiChatMessage> history, ChatMessage placeholder, CancellationToken ct, int startRound = 0)
    {
        try
        {
            var conversation = history;
            for (var round = startRound; round < MaxToolRounds; round++)
            {
                // 最后一个参数是会话级模型覆盖：输入框下拉选了就传，null 走设置页默认。
                var requestWatch = Stopwatch.StartNew();
                var result = await _ai.StreamChatAsync(conversation, Tools, OnDelta, ct, _sessionModel);
                requestWatch.Stop();
                WriteRequestAudit(result, requestWatch.ElapsedMilliseconds);

                if (!result.Ok)
                {
                    FailPlaceholder(placeholder, result.Error!);
                    return;
                }

                // 模型没有再要求工具 → 这轮的流式文本就是最终回复，正常收尾。
                if (!result.HasToolCalls)
                {
                    FinalizePlaceholder(placeholder);
                    return;
                }

                // —— 模型要求执行工具 ——
                // 1) 先把 assistant 的调用声明原样回放进对话：服务端靠 assistant.tool_calls
                //    与后续 tool 消息的 tool_call_id 一一对应，跳过这步下一轮请求会被拒。
                //    注意这里必须回放**全部**调用；写工具的回执等确认后才补。
                conversation.Add(new AiChatMessage
                {
                    Role = "assistant",
                    Content = result.Content.Length > 0 ? result.Content : null,
                    ToolCalls = result.ToolCalls.Select(t => new AiToolCallPayload
                    {
                        Id = t.Id,
                        Function = new AiFunctionCall { Name = t.Name, Arguments = t.Arguments }
                    }).ToList()
                });

                // 2) 读写分离：查询类立即执行（只读、可重复），写类先攒起来等用户确认。
                var writes = new List<AiToolCall>();
                foreach (var call in result.ToolCalls)
                {
                    if (IsWriteTool(call.Name))
                    {
                        writes.Add(call);
                        continue;
                    }

                    var (payload, note) = ExecuteToolAudited(call);
                    conversation.Add(new AiChatMessage { Role = "tool", ToolCallId = call.Id, Content = payload });
                    if (note is not null)
                        Messages.Add(new ChatMessage { Author = "Agent", Text = note, IsToolNote = true });
                }

                if (writes.Count == 0)
                {
                    SaveWorkspace();
                    continue;
                }

                // 3) 有写操作：本轮到此为止，界面出确认卡片。
                //    此时写工具还没有回执，不能继续请求模型（协议要求每个 tool_call 都有 tool 消息）。
                //    说明文本先正常收尾（落盘），再进入待确认状态。
                ShowConfirmCard(writes, conversation, round + 1);
                FinalizePlaceholder(placeholder);
                return;
            }

            // 轮次耗尽：把原因说清楚，用户可以选择继续对话让模型接着处理。
            FinalizePlaceholder(placeholder, "\n\n（连续执行工具已达上限，已暂停；你可以继续对话让我接着处理。）");
        }
        catch (OperationCanceledException)
        {
            FinalizePlaceholder(placeholder, "\n（已取消。）");
        }
        catch (Exception ex)
        {
            // 兜底：任何未预期异常都转成可读文案。异步任务里的异常若无人观察
            // 只会静默吞掉，这里显式转成界面反馈，用户至少知道「刚才没回成」。
            FailPlaceholder(placeholder, $"AI 对话出错：{ex.Message}");
        }
    }

    // ===================== 写操作确认 =====================
    // 状态机：模型返回写工具 → 只读工具先执行 → 暂存写工具与对话快照 → 出卡片 →
    // 确认（执行 + 补齐 tool 回执 + 续跑） / 取消（不执行） / 3 分钟超时 / 发新指令视为放弃。
    // 为什么必须暂存 conversation：assistant 的 tool_calls 已经回放进请求上下文，
    // 确认后要在这个上下文上继续，模型才知道自己刚才请求了什么。

    private PendingWrite? _pendingWrite;
    private DispatcherTimer? _pendingTimer;

    /// <summary>一批已声明、尚未执行的写工具调用及其对话快照。</summary>
    private sealed class PendingWrite
    {
        public List<AiToolCall> Calls { get; init; } = new();
        public List<AiChatMessage> Conversation { get; init; } = new();
        public int RoundsUsed { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
        public ChatMessage? Card { get; init; }
    }

    /// <summary>把写工具调用变成界面卡片，并启动 3 分钟有效期计时。</summary>
    private void ShowConfirmCard(List<AiToolCall> writes, List<AiChatMessage> conversation, int roundsUsed)
    {
        var detail = string.Join("\n", writes.Select((c, i) => $"{i + 1}. {DescribeCall(c)}"));
        var card = new ChatMessage
        {
            Author = "Agent",
            IsConfirmCard = true,
            ConfirmDetail = detail
        };
        Messages.Add(card);

        _pendingWrite = new PendingWrite
        {
            Calls = writes,
            Conversation = conversation,
            RoundsUsed = roundsUsed,
            ExpiresAt = DateTimeOffset.Now.AddMinutes(3),
            Card = card
        };
        StartPendingTimer();
    }

    /// <summary>确认执行：逐条执行写工具、补齐 tool 回执、落盘，然后从原轮次继续对话。</summary>
    private void ConfirmPending()
    {
        var pending = _pendingWrite;
        if (pending is null) return;
        // 计时器可能因 UI 线程繁忙而延迟触发，这里再校验一次绝对时间。
        if (DateTimeOffset.Now >= pending.ExpiresAt)
        {
            ExpirePending();
            return;
        }

        _pendingWrite = null;
        StopPendingTimer();
        FinishCard(pending.Card, "已确认，正在执行…");
        WriteConfirmAudit(pending, confirmed: true);

        try
        {
            foreach (var call in pending.Calls)
            {
                var (payload, note) = ExecuteToolAudited(call);
                pending.Conversation.Add(new AiChatMessage { Role = "tool", ToolCallId = call.Id, Content = payload });
                if (note is not null)
                    Messages.Add(new ChatMessage { Author = "Agent", Text = note, IsToolNote = true });
            }
            // 整批执行完统一落盘一次：与界面按钮保存走同一条链路。
            SaveWorkspace();
        }
        catch (Exception ex)
        {
            // 工具执行体理论上只会返回结构化错误，这里是最后一道兜底：
            // 已经改过的内存数据先落盘，再明确告知用户「部分可能已生效」，不继续请求模型。
            SaveWorkspace();
            AgentAudit.Append(new AgentAuditEntry
            {
                Kind = "tool",
                ToolName = "confirm_batch",
                Ok = false,
                Error = Truncate(ex.Message, 200)
            });
            Messages.Add(new ChatMessage
            {
                Author = "Agent",
                IsError = true,
                Text = $"执行写操作时出错：{ex.Message}。已中止后续步骤，已生效的改动已保存到本机缓存。"
            });
            IsAiBusy = false;
            return;
        }

        StartAgentRun(pending.Conversation, pending.RoundsUsed);
    }

    /// <summary>取消：不执行任何写操作，只留一条可见说明并写审计。</summary>
    private void CancelPending()
    {
        var pending = _pendingWrite;
        if (pending is null) return;
        _pendingWrite = null;
        StopPendingTimer();
        FinishCard(pending.Card, "已取消，未写入任何数据");
        WriteConfirmAudit(pending, confirmed: false);
        Messages.Add(new ChatMessage
        {
            Author = "Agent",
            IsToolNote = true,
            Text = "已取消。本次没有写入任何数据。"
        });
    }

    /// <summary>3 分钟未确认：卡片失效，写审计，不执行。</summary>
    private void ExpirePending()
    {
        var pending = _pendingWrite;
        if (pending is null) return;
        _pendingWrite = null;
        StopPendingTimer();
        FinishCard(pending.Card, "已过期，未执行");
        WriteConfirmAudit(pending, confirmed: false);
        Messages.Add(new ChatMessage
        {
            Author = "Agent",
            IsToolNote = true,
            Text = "确认已超时（3 分钟），本次操作未执行。需要的话请重新发送指令。"
        });
    }

    /// <summary>用户直接发了新指令：静默放弃待确认，不执行、不刷屏。</summary>
    private void AbandonPendingIfAny()
    {
        var pending = _pendingWrite;
        if (pending is null) return;
        _pendingWrite = null;
        StopPendingTimer();
        FinishCard(pending.Card, "已放弃，未执行");
        WriteConfirmAudit(pending, confirmed: false);
    }

    /// <summary>把卡片置为结束态并写明原因；按钮由 IsConfirmExpired 置灰。</summary>
    private static void FinishCard(ChatMessage? card, string state)
    {
        if (card is null) return;
        card.IsConfirmExpired = true;
        card.ConfirmState = state;
    }

    private void StartPendingTimer()
    {
        StopPendingTimer();
        _pendingTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
        _pendingTimer.Tick += (_, _) => ExpirePending();
        _pendingTimer.Start();
    }

    private void StopPendingTimer()
    {
        _pendingTimer?.Stop();
        _pendingTimer = null;
    }

    /// <summary>起一轮 Agent 请求：置忙态、建占位消息、订阅流式刷新。</summary>
    // 抽出来供 Send 与 ConfirmPending 共用，避免两处各写一遍忙态与占位消息。
    private void StartAgentRun(List<AiChatMessage> conversation, int startRound)
    {
        IsAiBusy = true;
        _aiCts?.Dispose();
        _aiCts = new CancellationTokenSource();
        var placeholder = new ChatMessage { Author = "Agent", Text = "正在输入…", IsStreaming = true };
        _streamTarget = placeholder;
        _streamStarted = false;
        Messages.Add(placeholder);
        EnsureFlushTimer().Start();
        _ = RunAgentLoopAsync(conversation, placeholder, _aiCts.Token, startRound);
    }

    // ===================== Agent 审计 =====================
    // 所有审计都是旁路：写失败只记 AgentAudit.LastError，不影响对话。

    private void WriteRequestAudit(AiStreamResult result, long durationMs)
    {
        var cfg = LocalCache.Data.Ai;
        AgentAudit.Append(new AgentAuditEntry
        {
            Kind = "request",
            Model = string.IsNullOrWhiteSpace(_sessionModel) ? cfg?.Model : _sessionModel,
            BaseUrlHost = HostOf(cfg?.BaseUrl),
            Ok = result.Ok,
            Error = result.Ok ? null : Truncate(result.Error, 200),
            DurationMs = durationMs
        });
    }

    /// <summary>执行一个工具并写审计；返回值与 ExecuteTool 完全一致。</summary>
    private (string Payload, string? Note) ExecuteToolAudited(AiToolCall call)
    {
        var watch = Stopwatch.StartNew();
        var result = ExecuteTool(call);
        watch.Stop();

        var ok = PayloadOk(result.Payload);
        AgentAudit.Append(new AgentAuditEntry
        {
            Kind = "tool",
            ToolName = call.Name,
            ArgsSummary = SummarizeArgs(call),
            Ok = ok,
            Error = ok ? null : Truncate(PayloadMessage(result.Payload), 200),
            DurationMs = watch.ElapsedMilliseconds,
            Target = TargetOf(call.Name, result.Payload)
        });
        return result;
    }

    private static void WriteConfirmAudit(PendingWrite pending, bool confirmed)
    {
        AgentAudit.Append(new AgentAuditEntry
        {
            Kind = "confirm",
            Confirmed = confirmed,
            ToolName = string.Join(",", pending.Calls.Select(c => c.Name)),
            ArgsSummary = Truncate(string.Join(" | ", pending.Calls.Select(SummarizeArgs)), 200),
            Ok = confirmed,
            Error = confirmed ? null : "用户未确认"
        });
    }

    private static string? HostOf(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        return Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri)
            ? uri.Host
            : Truncate(baseUrl, 60);
    }

    /// <summary>写操作白名单：只有这三个工具需要用户确认。</summary>
    private static bool IsWriteTool(string name) =>
        name is "create_customer" or "create_task" or "decide_task";

    /// <summary>payload 里的 ok 字段为 false 才算失败；查询结果没有 ok 字段，视为成功。</summary>
    private static bool PayloadOk(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return !doc.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.False;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static string PayloadMessage(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() ?? payload
                : payload;
        }
        catch (JsonException)
        {
            return payload;
        }
    }

    /// <summary>从工具结果里推导审计目标：create_* / decide_task 的 id。</summary>
    private static string? TargetOf(string toolName, string payload)
    {
        if (toolName is not ("create_customer" or "create_task" or "decide_task")) return null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String)
                return null;
            var value = id.GetString();
            return toolName == "create_customer" ? $"customer:{value}" : $"task:{value}";
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>参数摘要：只保留关键标识，自由文本截断，绝不记录完整对话或密钥。</summary>
    private static string SummarizeArgs(AiToolCall call)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return "{}";

            var parts = new List<string>();
            void Add(string key, int max = 40)
            {
                if (!root.TryGetProperty(key, out var el)) return;
                if (el.ValueKind == JsonValueKind.String)
                {
                    var value = el.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) parts.Add($"{key}={Truncate(value.Trim(), max)}");
                }
                else if (el.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number)
                {
                    parts.Add($"{key}={el}");
                }
            }

            switch (call.Name)
            {
                case "list_customers": Add("query"); break;
                case "list_tasks": Add("status"); break;
                case "create_customer": Add("name"); Add("code"); Add("type"); break;
                case "create_task": Add("title"); Add("customer"); Add("kind"); break;
                case "decide_task": Add("task_id"); Add("title"); Add("approve"); break;
            }

            return parts.Count == 0 ? "{}" : Truncate(string.Join(", ", parts), 200);
        }
        catch (JsonException)
        {
            return "<invalid-json>";
        }
    }

    /// <summary>确认卡片上的一行操作描述（面向用户，不暴露 JSON）。</summary>
    private static string DescribeCall(AiToolCall call)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments);
            var root = doc.RootElement;

            string S(string key) => root.ValueKind == JsonValueKind.Object
                                    && root.TryGetProperty(key, out var v)
                                    && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? string.Empty
                : string.Empty;

            return call.Name switch
            {
                "create_customer" => $"新建客户「{S("name")}」"
                    + (S("code").Length > 0 ? $"（编号 {S("code")}）" : "（编号自动生成）"),
                "create_task" => $"新建待办「{S("title")}」"
                    + (S("customer").Length > 0 ? $"，关联客户「{S("customer")}」" : "，不关联客户"),
                "decide_task" => (root.ValueKind == JsonValueKind.Object
                                    && root.TryGetProperty("approve", out var ap)
                                    && ap.ValueKind == JsonValueKind.False
                        ? "退回待办"
                        : "通过待办")
                    + (S("task_id").Length > 0 ? $"（{S("task_id")}）"
                        : S("title").Length > 0 ? $"（{S("title")}）" : string.Empty),
                _ => call.Name
            };
        }
        catch (JsonException)
        {
            return call.Name;
        }
    }

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= max ? text : text[..max] + "…";
    }

    // 结束一轮流式（正常完成 / 取消 / 超限）：清空缓冲剩余增量、停刷新计时器、
    // 解除「正在输入」标记、落盘并退出忙态。只在 UI 线程调用。
    private void FinalizePlaceholder(ChatMessage placeholder, string? append = null)
    {
        FlushStreamBuffer();
        _streamFlushTimer?.Stop();
        _streamTarget = null;
        if (!string.IsNullOrEmpty(append)) placeholder.Text += append;
        placeholder.IsStreaming = false;
        // 前面 SaveSoon 的防抖可能还没到期，这里再排一次，保证「发完立刻关程序」不丢最后一条。
        SaveWorkspace();
        IsAiBusy = false;
    }

    // 请求失败：占位消息转错误样式。一个字都没收到 → 整条替换成错误文案；
    // 已经流出来一半 → 保留已生成内容，把错误缀在尾部（内容是模型产物，仍不落盘以外的东西）。
    private void FailPlaceholder(ChatMessage placeholder, string error)
    {
        FlushStreamBuffer();
        _streamFlushTimer?.Stop();
        _streamTarget = null;
        if (!_streamStarted)
        {
            placeholder.Text = error;
            placeholder.IsError = true;
        }
        else
        {
            placeholder.Text += $"\n\n[{error}]";
        }
        placeholder.IsStreaming = false;
        IsAiBusy = false;
        // 错误消息不落盘（SaveWorkspace 跳过 IsError）：下次启动的对话历史里不会残留失败记录。
    }

    // 流式增量回调：发生在 AiChatService 的后台读流线程。这里只把增量安全地搬到 UI 线程
    // 的缓冲区，真正的正文拼接交给 FlushStreamBuffer（33ms 一帧合并刷新，避免高频重绘）。
    private void OnDelta(string piece) => Dispatcher.UIThread.Post(() => _streamBuffer.Append(piece));

    // 增量合并刷新计时器：懒创建、常驻复用（一轮对话 Start / 收尾 Stop）。
    private DispatcherTimer EnsureFlushTimer()
    {
        if (_streamFlushTimer is not null) return _streamFlushTimer;
        _streamFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _streamFlushTimer.Tick += (_, _) => FlushStreamBuffer();
        return _streamFlushTimer;
    }

    // 把缓冲里的增量拼进占位消息。首段增量做「整段替换」（顶掉「正在输入…」占位文案），
    // 之后全部追加。只在 UI 线程被调（计时器 Tick、Finalize、Fail）。
    private void FlushStreamBuffer()
    {
        if (_streamBuffer.Length == 0 || _streamTarget is null) return;
        var piece = _streamBuffer.ToString();
        _streamBuffer.Clear();
        var target = _streamTarget;
        target.Text = _streamStarted ? target.Text + piece : piece;
        _streamStarted = true;
        // 正文变了但集合没变，View 的 CollectionChanged 不会响；显式发事件让视图滚到底。
        ChatContentAppended?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>进入对话上下文的最大历史条数（不含 system），控制 token 体积的粗闸门。</summary>
    private const int MaxContextMessages = 20;

    // 把界面上的对话整理成协议消息：system 提示 + 最近至多 MaxContextMessages 条。
    // 四类消息不进上下文：IsError（错误提示）、IsToolNote（工具回执，业务结论已并入
    // 后续 assistant 文本）、IsStreaming（尚在生成中的占位消息）、IsConfirmCard（确认卡片，
    // 它的内容已经以 assistant tool_calls + tool 回执的形式留在 conversation 里）。
    private List<AiChatMessage> BuildAiHistory()
    {
        var history = new List<AiChatMessage> { new() { Role = "system", Content = BuildSystemPrompt() } };
        foreach (var m in Messages)
        {
            if (m.IsError || m.IsToolNote || m.IsStreaming || m.IsConfirmCard) continue;
            history.Add(new AiChatMessage { Role = m.IsUser ? "user" : "assistant", Content = m.Text });
        }
        // 只保留最近 N 条：RemoveRange(1, …) 是因为下标 0 是 system，永远保留。
        if (history.Count > MaxContextMessages + 1)
            history.RemoveRange(1, history.Count - MaxContextMessages - 1);
        return history;
    }

    // 系统提示：告诉模型自己的角色、今天的日期与工作区现状，以及工具的使用约定。
    // 工作区规模（几个客户、几条待办）每次发送都现算，模型不用先调 list_* 就能答总量问题。
    private string BuildSystemPrompt()
    {
        var pending = _allTasks.Count(t => t.IsPending);
        return $"""
            你是「Aster」工作台的 AI 助手 Agent，运行在一套客户服务 / 人事外包工作台的 Demo 演示应用里，当前操作者是「{OperatorName}」。
            今天是 {DateTime.Now:yyyy-MM-dd}。当前工作区共有 {_allCustomers.Count} 个客户、{_allTasks.Count} 条待办（其中 {pending} 条待处理）、{Metrics.Count} 条 KPI 指标。
            你可以调用工具直接查询和操作工作区数据，操作会立即保存到本地：
            - list_customers / list_tasks：先查再改，不要凭空猜测客户名、编号或待办标题；
            - create_customer：只有 name 必填，其余字段可省略；用户没有给出名称时先向用户确认；
            - create_task / decide_task：审批前先用 list_tasks 找到目标待办，再提交通过或退回。
            要求：用与用户一致的语言（默认简体中文）简洁回答；执行写操作后用一句话向用户汇报结果；
            信息不足时先追问，不要编造数据。
            """;
    }

    // ===================== AI 工具（Function Calling） =====================
    // 五个工具的名字与参数模式（JSON Schema）会原样发给模型；执行体全部复用界面按钮走的
    // 同一批数据操作（集合刷新 + SaveWorkspace），保证「AI 改的」与「手点的」是同一份数据、
    // 同一条落盘链路 —— 工具从不直接碰 LocalCache 文件。
    // 参数名用 snake_case：模型对这种风格的函数参数遵循度普遍更好（OpenAI 官方示例同款）。

    /// <summary>发给模型的工具清单。只读一次构造；Schema() 把匿名对象转成 JSON 元素。</summary>
    private AiToolDefinition[] Tools { get; } =
    {
        new("list_customers",
            "查询当前工作区的客户列表，返回编号、名称、类型、负责人、城市、状态、在册人数与待处理数。",
            Schema(new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "可选；按客户名称或编号模糊匹配的关键字" }
                },
                required = Array.Empty<string>()
            })),
        new("list_tasks",
            "查询待办/审批列表，返回编号、标题、类型、所属客户、期限、状态与处理说明。",
            Schema(new
            {
                type = "object",
                properties = new
                {
                    status = new { type = "string", description = "可选；待处理 / 已通过 / 已退回，缺省返回全部" }
                },
                required = Array.Empty<string>()
            })),
        new("create_customer",
            "新建一条客户档案。只有 name 必填；编号留空会自动生成（KH-001 递增）。",
            Schema(new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string", description = "客户全称（必填）" },
                    short_name = new { type = "string", description = "简称，缺省用全称" },
                    code = new { type = "string", description = "客户编号，缺省自动生成" },
                    type = new { type = "string", description = "服务类型：劳务派遣 / 业务外包 / 人事代理" },
                    owner = new { type = "string", description = "负责人，缺省为当前登录账号" },
                    city = new { type = "string", description = "所在城市" },
                    contact = new { type = "string", description = "客户联系人" },
                    service = new { type = "string", description = "服务内容" },
                    state = new { type = "string", description = "核对状态：待复核 / 已完成 / 待补材料 / 暂无核对" }
                },
                required = new[] { "name" }
            })),
        new("create_task",
            "新建一条待办/审批事项。只有 title 必填，默认状态为待处理。",
            Schema(new
            {
                type = "object",
                properties = new
                {
                    title = new { type = "string", description = "待办标题（必填）" },
                    kind = new { type = "string", description = "类型：审批 / 核对 / 资料 / 协作，缺省审批" },
                    customer = new { type = "string", description = "所属客户名称，可省略" },
                    due = new { type = "string", description = "期限：今日 / 明日 / 本周 / 本月，缺省今日" },
                    note = new { type = "string", description = "处理说明" }
                },
                required = new[] { "title" }
            })),
        new("decide_task",
            "对一条「待处理」中的待办执行审批：通过或退回。优先用 task_id 精确定位，其次按标题匹配。",
            Schema(new
            {
                type = "object",
                properties = new
                {
                    task_id = new { type = "string", description = "待办编号（list_tasks 返回的 id）" },
                    title = new { type = "string", description = "待办标题；没有 task_id 时按标题匹配待处理项" },
                    approve = new { type = "boolean", description = "true=通过，false=退回（必填）" },
                    reason = new { type = "string", description = "处理说明（退回原因等），可省略" }
                },
                required = new[] { "approve" }
            }))
    };

    /// <summary>匿名对象 → JSON 元素。属性名本身就是协议要求的小写，无需命名策略。</summary>
    private static JsonElement Schema(object shape) => JsonSerializer.SerializeToElement(shape);

    // 工具执行分发：入参是模型给出的调用（名字 + JSON 字符串参数），返回
    // （回传给模型的 JSON 结果， 界面回执文案）。查询类不产生回执（note = null）。
    // 前提：在 UI 线程调用（RunAgentLoopAsync 的 await 回到 UI 线程后串行执行）；
    // 参数解析失败、未知工具都返回结构化错误 JSON，让模型自己看到原因并决定重试还是改口。
    private (string Payload, string? Note) ExecuteTool(AiToolCall call)
    {
        try
        {
            using var args = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments);
            var root = args.RootElement;
            switch (call.Name)
            {
                case "list_customers": return (ToolListCustomers(root), null);
                case "list_tasks": return (ToolListTasks(root), null);
                case "create_customer": return ToolCreateCustomer(root);
                case "create_task": return ToolCreateTask(root);
                case "decide_task": return ToolDecideTask(root);
                default:
                    return (JsonOk(false, $"未知工具：{call.Name}"), null);
            }
        }
        catch (JsonException)
        {
            return (JsonOk(false, "工具参数不是合法 JSON，请修正后重新调用。"), null);
        }
    }

    /// <summary>工具结果的统一外壳：ok + 人读 message，模型能直接理解成败。</summary>
    private static string JsonOk(bool ok, string message) => JsonSerializer.Serialize(new { ok, message });

    // 从工具参数根对象里取字符串字段：字段缺失 / 类型不对 / 空白一律返回空串，
    // 由各工具自己决定空串的语义（必填报错、可选用默认值）。
    private static string GetStr(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object) return string.Empty;
        if (!root.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String) return string.Empty;
        var s = v.GetString();
        return string.IsNullOrWhiteSpace(s) ? string.Empty : s.Trim();
    }

    private string ToolListCustomers(JsonElement root)
    {
        var q = GetStr(root, "query").ToLowerInvariant();
        var rows = _allCustomers
            .Where(c => q.Length == 0
                        || c.Name.ToLowerInvariant().Contains(q)
                        || c.Code.ToLowerInvariant().Contains(q))
            .Select(c => new
            {
                id = c.Id, code = c.Code, name = c.Name, type = c.Type,
                owner = c.Owner, city = c.City, state = c.State,
                people = c.People, pending = c.Pending, date = c.Date
            })
            .ToList();
        return JsonSerializer.Serialize(new { count = rows.Count, customers = rows });
    }

    private string ToolListTasks(JsonElement root)
    {
        var status = GetStr(root, "status");
        var rows = _allTasks
            .Where(t => status.Length == 0 || t.Status == status)
            .Select(t => new
            {
                id = t.Id, title = t.Title, kind = t.Kind,
                customer = t.CustomerName, due = t.Due,
                status = t.Status, note = t.Note
            })
            .ToList();
        return JsonSerializer.Serialize(new { count = rows.Count, tasks = rows });
    }

    // 与表单 SaveCustomer 的新建分支同构：同样的校验（名称必填、编号查重）、同样的默认值
    // 与「插入到最前面」。刻意不复用表单字段（Form*）：工具调用必须直接拿参数干活，
    // 不能去开一张浮层往里塞值 —— 那会让「AI 操作」与「用户正在填的表单」互相踩踏。
    private (string Payload, string? Note) ToolCreateCustomer(JsonElement root)
    {
        var name = GetStr(root, "name");
        if (name.Length == 0)
            return (JsonOk(false, "name 必填：请提供客户全称。"), null);

        // 编号：留空自动递增；给了就查重（与界面保存同一规则，包括 OrdinalIgnoreCase）。
        var code = GetStr(root, "code");
        if (code.Length == 0) code = NextCustomerCode();
        else if (_allCustomers.Any(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)))
            return (JsonOk(false, $"编号 {code} 已存在，请换一个或省略 code 让系统自动生成。"), null);

        var now = DateTime.Now;
        var owner = GetStr(root, "owner");
        // 默认负责人取当前登录账号，而不是写死的人名：否则 AI 新建的客户会挂到一个不存在的用户上。
        if (owner.Length == 0) owner = OperatorName;
        var type = GetStr(root, "type");
        if (type.Length == 0) type = CustomerTypes[0];
        var state = GetStr(root, "state");
        if (state.Length == 0) state = CustomerStates[0];
        var shortName = GetStr(root, "short_name");
        if (shortName.Length == 0) shortName = name;

        var item = new CustomerItem
        {
            Id = NewId("c"),
            Name = name,
            Short = shortName,
            Code = code,
            Type = type,
            Owner = owner,
            City = GetStr(root, "city"),
            Contact = GetStr(root, "contact"),
            Service = GetStr(root, "service"),
            Tax = "已登记",
            File = string.Empty,
            Date = now.ToString("MM-dd HH:mm"),
            State = state,
            People = 0,
            Pending = 0,
            OpenCommand = OpenCustomerCommand,
            Activities = new List<ActivityItem>
            {
                new() { Text = "新建客户档案", Time = $"{now:MM-dd HH:mm} · {owner}" }
            }
        };

        _allCustomers.Insert(0, item);
        RefreshCustomers();
        RefreshCustomerOptions();
        return (JsonSerializer.Serialize(new { ok = true, id = item.Id, code = item.Code, name = item.Name }),
                $"已新建客户「{item.Name}」（{item.Code}），可在客户工作空间查看。");
    }

    private (string Payload, string? Note) ToolCreateTask(JsonElement root)
    {
        var title = GetStr(root, "title");
        if (title.Length == 0)
            return (JsonOk(false, "title 必填：请提供待办标题。"), null);

        // kind / due 收敛到界面下拉的合法取值：工具参数自由文本（如「尽快」）会被拉回白名单，
        // 避免文件里混进界面上选不出来的状态值。
        var kind = GetStr(root, "kind");
        if (!TaskKinds.Contains(kind)) kind = TaskKinds[0];
        var due = GetStr(root, "due");
        if (!TaskDues.Contains(due)) due = TaskDues[0];

        var item = new TaskItem
        {
            Id = NewId("t"),
            Title = title,
            Kind = kind,
            CustomerName = GetStr(root, "customer"),
            Due = due,
            Status = "待处理",
            Note = GetStr(root, "note"),
            ApproveCommand = ApproveCommand,
            RejectCommand = RejectCommand
        };

        _allTasks.Insert(0, item);
        // 新建的待办一定是「待处理」，与界面保存一致：切回待处理筛选，让用户立刻看到它。
        TaskFilter = "待处理";
        RefreshTasks();
        return (JsonSerializer.Serialize(new { ok = true, id = item.Id, title = item.Title, status = item.Status }),
                $"已新建待办「{item.Title}」（{item.Kind} · {item.Due}），状态待处理。");
    }

    private (string Payload, string? Note) ToolDecideTask(JsonElement root)
    {
        // approve 是布尔且必填：ValueKind 直接区分 true / false / 缺失，比 Convert 更严格。
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("approve", out var apEl)
            || (apEl.ValueKind != JsonValueKind.True && apEl.ValueKind != JsonValueKind.False))
            return (JsonOk(false, "approve 必填：true 表示通过，false 表示退回。"), null);
        var approve = apEl.ValueKind == JsonValueKind.True;

        // 定位：先按 task_id 精确找，再按标题找；只匹配「待处理」的——与界面按钮一致，
        // 已处理完的待办不允许重复审批。
        var id = GetStr(root, "task_id");
        var title = GetStr(root, "title");
        var task = _allTasks.FirstOrDefault(t => t.IsPending
            && (id.Length > 0 && t.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));
        if (task is null && title.Length > 0)
            task = _allTasks.FirstOrDefault(t => t.IsPending
                && t.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
        if (task is null)
            return (JsonOk(false, "未找到匹配的「待处理」待办；请先用 list_tasks 查询确认编号或标题。"), null);

        var reason = GetStr(root, "reason");
        // 与界面 Decide 同一状态机：Status 的 setter 会连带刷新标签配色与按钮可见性；
        // Note 用模型给的原因，缺省时沿用界面同款文案。
        task.Status = approve ? "已通过" : "已退回";
        // 缺省说明与界面保持一致：演示环境不会真的派生补材料待办，文案不能那样承诺。
        task.Note = reason.Length > 0
            ? reason
            : approve ? "审批已通过，结果只写入本机演示缓存。" : "已记录退回原因；演示环境不会自动新增待办。";
        RefreshTasks();
        return (JsonSerializer.Serialize(new { ok = true, id = task.Id, title = task.Title, status = task.Status }),
                $"已{(approve ? "通过" : "退回")}待办「{task.Title}」。");
    }

    // ===================== 客户列表 =====================
    public ObservableCollection<CustomerItem> Customers { get; }
    public ICommand OpenCustomerCommand { get; }
    public ICommand GoWorkflowCommand { get; }
    public ICommand SelectCommand { get; }

    public string Query
    {
        get => _query;
        // 搜索框的 TextBox 双向绑定它。每次击键都会走一遍 RefreshCustomers，
        // 演示数据只有 4 条，这点重建开销可以忽略；换成大列表就该改成延迟过滤。
        set
        {
            if (!Set(ref _query, value)) return;
            RefreshCustomers();
        }
    }

    public string Filter
    {
        get => _filter;
        // ComboBox 的 SelectedItem 双向绑定它。初始值「全部客户」必须出现在 Filters 数组里，
        // 否则 ComboBox 找不到匹配项会显示空白。
        set
        {
            if (!Set(ref _filter, value)) return;
            RefreshCustomers();
        }
    }

    public string CustomerCount => $"显示 {Customers.Count} 个客户";
    public string CustomerTotal => $"本地缓存 · 共 {_allCustomers.Count} 个客户";
    public string[] Filters { get; } = { "全部客户", "有待处理事项", "我负责的客户" };

    /// <summary>列表里有没有客户。空态提示绑它（true 时显示表格，false 时显示一行说明）。</summary>
    public bool HasCustomers => Customers.Count > 0;

    // 调用方：Query / Filter 的 setter、构造函数（集合初始化之后）。
    // 做完之后：Customers 换成过滤结果，CustomerCount / HasCustomers 重算并通知；_allCustomers 保持全量不变。
    private void RefreshCustomers()
    {
        // 步骤 1：查询词统一转小写再比较；空串表示不过滤。用 ToLowerInvariant 而不是当前区域的
        // ToLower，是为了避免土耳其语等区域把 I/i 转出不同结果这类经典坑。
        var q = _query.Trim().ToLowerInvariant();
        // 步骤 2：三重条件取交集。后两个筛选用「不等于该选项就放行」的写法，
        // 省掉为「全部客户」再写一个分支；延迟执行，直到下面 foreach 才真正求值。
        var list = _allCustomers.Where(c =>
            (q.Length == 0 || c.Name.ToLowerInvariant().Contains(q) || c.Code.ToLowerInvariant().Contains(q))
            && (_filter != "有待处理事项" || c.Pending > 0)
            // 「我负责的客户」跟当前登录账号走：以前固定比「张客服」，换个账号登录就永远筛不出数据。
            && (_filter != "我负责的客户" || c.Owner == OperatorName));

        // 步骤 3：Clear + 逐条 Add，而不是整体换实例 —— XAML 绑的是这个只读属性，换实例界面收不到。
        // ObservableCollection 的 Reset/Add 事件会让 ItemsControl 自己重建行。
        Customers.Clear();
        foreach (var c in list) Customers.Add(c);
        // 步骤 4：CustomerCount / HasCustomers 都是计算属性，集合变了不会自动通知，必须手发。
        // CustomerTotal 读的是 _allCustomers.Count，与过滤无关，所以不用发。
        OnPropertyChanged(nameof(CustomerCount));
        OnPropertyChanged(nameof(HasCustomers));
    }

    // ===================== 客户详情 =====================
    public CustomerItem? Current
    {
        get => _current;
        // 只在 OpenCustomer 里赋值，所以 setter 私有。九个派生属性全部手发：
        // 详情页绑的是 CurrentName / CurrentMeta / CurrentFile 这些摊平后的只读属性，
        // 没人绑 Current 本身，漏发哪条就会出现「换了客户但某一格还是旧值」。
        // HasCurrent 目前没有被 XAML 使用，属于预留状态位。
        private set
        {
            if (!Set(ref _current, value)) return;
            OnPropertyChanged(nameof(HasCurrent));
            OnPropertyChanged(nameof(CurrentName));
            OnPropertyChanged(nameof(CurrentMeta));
            OnPropertyChanged(nameof(CurrentFile));
            OnPropertyChanged(nameof(CurrentCity));
            OnPropertyChanged(nameof(CurrentContact));
            OnPropertyChanged(nameof(CurrentService));
            OnPropertyChanged(nameof(CurrentTax));
            OnPropertyChanged(nameof(CurrentPeopleText));
            // Workflow 顶部「关联客户」也读 Current，换客户时不补发就会停在上一家。
            OnPropertyChanged(nameof(WfCustomerName));
            OnPropertyChanged(nameof(WfCustomerCode));
            OnPropertyChanged(nameof(HasWfCustomer));
        }
    }

    public bool HasCurrent => _current is not null;
    public string CurrentName => _current?.Name ?? string.Empty;
    public string CurrentMeta => _current is null ? string.Empty : $"{_current.Code} · {_current.Type} · 负责人：{_current.Owner}";
    public string CurrentPeopleText => _current is null ? string.Empty : $"{_current.People} 人";
    public string CurrentFile => _current?.File ?? string.Empty;
    public string CurrentCity => _current?.City ?? string.Empty;
    public string CurrentContact => _current?.Contact ?? string.Empty;
    public string CurrentService => _current?.Service ?? string.Empty;
    public string CurrentTax => _current?.Tax ?? string.Empty;

    public bool IsTabBase => _detailTab == "base";
    public bool IsTabPeople => _detailTab == "people";
    public bool IsTabRuns => _detailTab == "runs";
    public bool IsTabFiles => _detailTab == "files";

    public ICommand TabCommand { get; }
    public ICommand BackCommand { get; }

    public ObservableCollection<ActivityItem> Activities { get; }
    public ObservableCollection<PersonRow> PeopleRows { get; }
    public ObservableCollection<RunRow> RunRows { get; }
    public ObservableCollection<FileRow> FileRows { get; }
    public bool HasRunRows => RunRows.Count > 0;

    // 另外三个页签的空态判据。以前只有核对页有提示，人员 / 文件 / 最近更新为空时是纯白板，
    // 在空演示数据下看起来像界面坏了；这里补成和 HasRunRows 同一套写法。
    public bool HasPeopleRows => PeopleRows.Count > 0;
    public bool HasFileRows => FileRows.Count > 0;
    public bool HasActivities => Activities.Count > 0;

    // 唯一进入客户详情的路径：OpenCustomerCommand、Select 的详情 key 都最终走这里。
    // 四个步骤的顺序不能换，否则会出现一帧「标题已是新客户、表格还是旧数据」或反过来。
    private void OpenCustomer(CustomerItem customer, string tab)
    {
        // 步骤 1：先换 Current 并发派生通知。此时页面还没显示，但标题/元信息已经是新客户。
        Current = customer;
        // 步骤 2：定页签，只改 _detailTab 并通知四个 IsTabXxx，不碰集合。
        SelectTab(tab);
        // 步骤 3：重建四个详情集合（最近更新 / 人员 / 核对 / 文件）。
        FillDetail(customer);
        // 步骤 4：最后才让详情页可见 —— 数据全部就位后再布局，用户看不到空表。
        Go(AdminPage.Detail);
    }

    // 调用方：TabCommand（四个页签按钮）与 OpenCustomer。
    // 不做 key 校验：传入未知字符串时四个 IsTabXxx 全为 false，详情页只剩标题、下面一片空。
    // 这是「调用方约定」而不是防御点，现有调用点都传编译期写死的四个值。
    private void SelectTab(string tab)
    {
        _detailTab = tab;
        OnPropertyChanged(nameof(IsTabBase));
        OnPropertyChanged(nameof(IsTabPeople));
        OnPropertyChanged(nameof(IsTabRuns));
        OnPropertyChanged(nameof(IsTabFiles));
    }

    // 调用方：OpenCustomer。前提：c 非空，四个集合已初始化。
    // 做完之后：四个集合的内容 = 该客户在本地缓存里存的 Activities / Staff / Runs / Files。
    // 关键变化（v2）：这里不再「按档案字段编造行数据」，只是把缓存里的列表复制到界面集合。
    // 想改某个客户的核对记录 / 人员 / 文件，改 local-cache.json 对应客户的数组即可。
    private void FillDetail(CustomerItem c)
    {
        // 步骤 1：最近更新。
        Activities.Clear();
        foreach (var a in c.Activities)
            Activities.Add(new ActivityItem { Text = a.Text, Time = a.Time });

        // 步骤 2：人员记录。
        PeopleRows.Clear();
        foreach (var p in c.Staff)
            PeopleRows.Add(new PersonRow
            {
                No = p.No,
                Name = p.Name,
                IdInfo = p.IdInfo,
                Customer = p.Customer,
                Status = p.Status,
                Source = p.Source
            });

        // 步骤 3：核对记录。
        RunRows.Clear();
        foreach (var r in c.Runs)
            RunRows.Add(new RunRow
            {
                Batch = r.Batch,
                Period = r.Period,
                Version = r.Version,
                Status = r.Status,
                Pending = r.Pending,
                Date = r.Date
            });
        // 步骤 4：HasRunRows 由 RunRows.Count 推导，集合变动不会自动通知它 ——
        // 这里手动补一条，否则「暂无核对记录」的提示不会随客户切换而显隐。
        OnPropertyChanged(nameof(HasRunRows));

        // 步骤 5：文件与产出。
        FileRows.Clear();
        foreach (var f in c.Files)
            FileRows.Add(new FileRow
            {
                Name = f.Name,
                Purpose = f.Purpose,
                Related = f.Related,
                Time = f.Time,
                Tone = f.Tone
            });

        // 步骤 6：另外三个空态判据同样要手动通知，否则切换客户后提示行不会显隐。
        OnPropertyChanged(nameof(HasPeopleRows));
        OnPropertyChanged(nameof(HasFileRows));
        OnPropertyChanged(nameof(HasActivities));
    }

    // ===================== 协作与审批 =====================
    public ObservableCollection<TaskItem> Tasks { get; }
    public ICommand TaskFilterCommand { get; }
    public ICommand ApproveCommand { get; }
    public ICommand RejectCommand { get; }

    public string TaskFilter
    {
        get => _taskFilter;
        // 只能由 TaskFilterCommand 改（setter 私有），取值只有「待处理」「全部」两种。
        // 两个派生 bool 驱动胶囊按钮的 Classes.selected，值没变时 Set 返回 false，连通知都不发。
        private set
        {
            if (!Set(ref _taskFilter, value)) return;
            OnPropertyChanged(nameof(IsTaskPending));
            OnPropertyChanged(nameof(IsTaskAll));
        }
    }

    public bool IsTaskPending => _taskFilter == "待处理";
    public bool IsTaskAll => _taskFilter == "全部";
    public int PendingCount => _allTasks.Count(t => t.IsPending);
    public string TaskSummary => $"{Tasks.Count} 项 · 数量随处理结果更新";

    /// <summary>当前筛选下有没有任务。空态提示绑它（false 时显示一行说明而不是空白）。</summary>
    public bool HasTasks => Tasks.Count > 0;

    // 调用方：构造函数、TaskFilterCommand、Decide。前提：_allTasks 已由 LoadWorkspace 填好（可能是空的）。
    // 做完之后：Tasks 换成过滤后的同一批实例（不克隆），并重算 TaskSummary / PendingCount / HasTasks。
    // 因为放的是原对象，审批改了 Status 之后，「全部」视图里同一行会同步变色，不需要重建对象。
    private void RefreshTasks()
    {
        // 「全部」不过滤；其余任何取值都按 IsPending 过滤（当前 UI 只会传「待处理」）。
        var list = _allTasks.Where(t => _taskFilter == "全部" || t.IsPending);
        Tasks.Clear();
        foreach (var t in list) Tasks.Add(t);
        // 三个计算属性都依赖 Tasks.Count 与源列表，集合变了不会自动通知，逐条补发。
        OnPropertyChanged(nameof(TaskSummary));
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(HasTasks));
    }

    // 调用方：ApproveCommand / RejectCommand，参数是 XAML 用 CommandParameter="{Binding}" 传回的 TaskItem。
    // 做完之后：该任务状态变为已通过/已退回并写入 Note，随后从「待处理」列表里消失。
    private void Decide(TaskItem task, string status)
    {
        // 步骤 1：改 Status。它的 setter 会连带通知 IsPending / StatusBrush / StatusBackground，
        // 所以状态标签立刻变色、右侧那组审批按钮立刻隐藏。
        task.Status = status;
        // 步骤 2：写 Note。Note 是普通自动属性，不发通知；它最终能显示出来，
        // 靠的是步骤 3 的集合重建让行容器重新绑定 —— 顺序反了或省掉步骤 3，界面就不会更新。
        // 文案里说「新增一条补材料待办」，但代码并没有真的往 _allTasks 里加任务，只是承诺。
        task.Note = status == "已退回"
            ? "已记录退回原因，并新增一条补材料待办。"
            : "审批已通过，结果已同步到工作台。";
        // 步骤 3：重建列表。状态已不是「待处理」，于是在「待处理」筛选下这一条会消失。
        RefreshTasks();
        // 步骤 4：把处理结果写回本地缓存。整体写回全量工作区（含这条任务的 status / note），
        // 下次启动按原样读出来渲染，所以处理过的任务不会再回到「待处理」。
        SaveWorkspace();
    }

    // ===================== Workflow =====================
    public int WfStep
    {
        get => _wfStep;
        // 三步向导的当前步（0/1/2）。setter 私有，只能由 StartRun / ResolveRun / WfRestartCommand 改。
        // 一次变更要发 9 条通知：3 个 IsWfStepX 控制三块面板的显隐，3 个 StepBg + 3 个 StepFg
        // 控制顶部三枚胶囊的底色与字色 —— 九个全是读 _wfStep 的计算属性，Set 只发 WfStep 自己。
        private set
        {
            if (!Set(ref _wfStep, value)) return;
            OnPropertyChanged(nameof(IsWfStep0));
            OnPropertyChanged(nameof(IsWfStep1));
            OnPropertyChanged(nameof(IsWfStep2));
            OnPropertyChanged(nameof(StepBg0));
            OnPropertyChanged(nameof(StepBg1));
            OnPropertyChanged(nameof(StepBg2));
            OnPropertyChanged(nameof(StepFg0));
            OnPropertyChanged(nameof(StepFg1));
            OnPropertyChanged(nameof(StepFg2));
        }
    }

    // 步骤指示条：当前步骤高亮，其余灰底
    // 高亮 = 品牌蓝 16% 底 + #A9BAFF 字；未激活 = 白色 7% 底 + #8B94A8 字。
    // 两套颜色写死在这里而不是做成样式，是因为「激活/未激活」取决于 _wfStep，属于 VM 状态而非视觉主题。
    public IBrush StepBg0 => _wfStep == 0 ? Palette.Alpha(Palette.AccentColor, 0.16) : Palette.Alpha("#FFFFFF", 0.07);
    public IBrush StepBg1 => _wfStep == 1 ? Palette.Alpha(Palette.AccentColor, 0.16) : Palette.Alpha("#FFFFFF", 0.07);
    public IBrush StepBg2 => _wfStep == 2 ? Palette.Alpha(Palette.AccentColor, 0.16) : Palette.Alpha("#FFFFFF", 0.07);
    public IBrush StepFg0 => _wfStep == 0 ? Palette.Solid("#A9BAFF") : Palette.Solid("#8B94A8");
    public IBrush StepFg1 => _wfStep == 1 ? Palette.Solid("#A9BAFF") : Palette.Solid("#8B94A8");
    public IBrush StepFg2 => _wfStep == 2 ? Palette.Solid("#A9BAFF") : Palette.Solid("#8B94A8");

    public bool IsWfStep0 => _wfStep == 0;
    public bool IsWfStep1 => _wfStep == 1;
    public bool IsWfStep2 => _wfStep == 2;

    public string WfSkill
    {
        get => _wfSkill;
        set => Set(ref _wfSkill, value);
    }

    public string WfPeriod
    {
        get => _wfPeriod;
        set => Set(ref _wfPeriod, value);
    }

    public bool WfAccepted
    {
        get => _wfAccepted;
        set => Set(ref _wfAccepted, value);
    }

    public string WfNote
    {
        get => _wfNote;
        set => Set(ref _wfNote, value);
    }

    public string WfError
    {
        get => _wfError;
        private set
        {
            if (!Set(ref _wfError, value)) return;
            OnPropertyChanged(nameof(HasWfError));
        }
    }

    public bool HasWfError => !string.IsNullOrEmpty(_wfError);
    public string[] Skills { get; } = { "个税", "社保", "公积金", "商保" };
    public ObservableCollection<ResultRow> WfRows { get; } = new();
    public ICommand WfStartCommand { get; }
    public ICommand WfResolveCommand { get; }
    public ICommand WfArchiveCommand { get; }
    public ICommand WfRestartCommand { get; }

    // 向导顶部「关联客户」显示的名字。没有客户时明确写「未选择客户」，
    // 以前兜底成「青禾新能源有限公司 / KH-001」会让空工作区看起来像真有一家客户。
    public string WfCustomerName => Current?.Name ?? "未选择客户";
    public string WfCustomerCode => Current?.Code ?? "—";

    /// <summary>Workflow 是否已关联到一个真实客户（空工作区里为 false）。</summary>
    public bool HasWfCustomer => Current is not null;

    // 调用方：「开始样例核对」按钮（WfStartCommand）。
    // 前提：WfAccepted 必须为 true —— 这是「不读取真实附件」的免责勾选，没勾就不让跑。
    // 做完之后：生成 3 条样例结果、清空错误、WfStep 从 0 推到 1，面板由第一步切到第二步。
    private void StartRun()
    {
        // 没有客户就没有可关联的核对任务：给一句可操作的提示，而不是拿假客户名继续跑。
        if (Current is null)
        {
            WfError = "演示提示：当前还没有客户。请先在「客户工作空间」新建一位客户，再发起核对。";
            return;
        }
        if (!WfAccepted)
        {
            // 校验失败也要有反馈：WfError 一非空，HasWfError 跟着变 true，红色提示行显形。
            WfError = "请先确认使用虚构样例。";
            // 提前 return，不推进步骤，也不生成数据。
            return;
        }

        // 清掉上一次的错误，避免「重试成功后红字还挂着」。
        WfError = string.Empty;
        // 先清空再灌样例：重复点「开始」不会把结果叠成 6 条。
        WfRows.Clear();
        // 三条固定样例：A、B 的业务侧与系统侧不一致（80/100、45/40），C 一致（120/120）。
        // Result 与 Status 都是写死的展示文案，不是由金额算出来的 —— 改金额不会自动改判定。
        WfRows.Add(new ResultRow { Person = "演示员工 A", Business = 80, System = 100, Result = "金额差异", Status = "待复核" });
        WfRows.Add(new ResultRow { Person = "演示员工 B", Business = 45, System = 40, Result = "金额差异", Status = "待复核" });
        WfRows.Add(new ResultRow { Person = "演示员工 C", Business = 120, System = 120, Result = "金额一致", Status = "本项通过" });
        // 最后推进步骤：1 表示进入「复核差异」。
        WfStep = 1;
    }

    // 调用方：「确认两条差异已复核」按钮（WfResolveCommand）。
    // 前提：WfNote 非空（必填说明）。做完之后：所有行状态改为「已复核」，WfStep 推到 2。
    private void ResolveRun()
    {
        if (string.IsNullOrWhiteSpace(WfNote))
        {
            WfError = "请填写复核说明。";
            return;
        }

        WfError = string.Empty;
        // 逐行「用新对象替换旧对象」而不是改 Status：ResultRow 的属性是 init-only，创建后不能改。
        // 用索引赋值会触发 ObservableCollection 的 Replace 通知，对应行会重新绑定、立刻变色。
        for (var i = 0; i < WfRows.Count; i++)
        {
            var row = WfRows[i];
            WfRows[i] = new ResultRow
            {
                Person = row.Person,
                Business = row.Business,
                System = row.System,
                Result = row.Result,
                Status = "已复核"
            };
        }
        // 注意这里没有校验「是否真的存在差异」：哪怕三条全一致，也可以直接进入归档步。
        WfStep = 2;
    }

    // 调用方：「确认归档到客户空间」按钮（WfArchiveCommand）。
    // 做完之后：只清错误并跳回客户工作空间；WfStep / WfRows / WfAccepted 都不复位，
    // 所以下次再进 Workflow 仍停在第 3 步的「复核已完成」界面，要重新跑得先点「重新开始」。
    private void ArchiveRun()
    {
        WfError = string.Empty;
        Go(AdminPage.Customers);
    }

    // ===================== KPI =====================
    public ObservableCollection<MetricRow> Metrics { get; } = new();

    /// <summary>KPI 列表是否有内容。空态提示绑它。</summary>
    public bool HasMetrics => Metrics.Count > 0;

    // 指标已经不在这里构造了：内容来自本地缓存的 workspace.metrics（见 LoadWorkspace / SaveWorkspace）。
    // 想加一行指标点 KPI 页底部的「新增指标」，或直接改文件 —— 代码里没有兜底的三条演示值。

    // ===================== 新建：客户 / 待办 / 指标 =====================
    // 这一节是 v2.1 补的「写入端」：v2 把数据全搬进缓存文件后，界面只能读不能写，
    // 空工作区只能靠手改 JSON 才能有内容。现在三个入口各自把「表单字段 → 领域对象 → 集合 → 磁盘」
    // 走完整条链，顺序固定为：校验 → 改集合 → RefreshXxx → SaveWorkspace → 关表单。
    // 反过来（先存盘再刷新）会出现「文件里有、界面没有」的一帧，且保存失败时更难察觉。

    public ICommand OpenCustomerFormCommand { get; }
    public ICommand CancelCustomerFormCommand { get; }
    public ICommand SaveCustomerCommand { get; }
    public ICommand EditCustomerCommand { get; }
    public ICommand DeleteCustomerCommand { get; }
    public ICommand ConfirmDeleteCommand { get; }
    public ICommand CancelDeleteCommand { get; }
    public ICommand OpenTaskFormCommand { get; }
    public ICommand CancelTaskFormCommand { get; }
    public ICommand SaveTaskCommand { get; }
    public ICommand AddMetricCommand { get; }

    /// <summary>客户表单是否展开（新建与编辑共用同一张浮层）。</summary>
    public bool IsCustomerFormOpen
    {
        get => _isCustomerFormOpen;
        private set => Set(ref _isCustomerFormOpen, value);
    }

    /// <summary>表单标题：新建 / 编辑。打开表单时手发通知（它只依赖 _editingCustomerId）。</summary>
    public string CustomerFormTitle => _editingCustomerId is null ? "新建客户" : "编辑客户";

    /// <summary>表单副标题。编辑态要额外说明「详情页的四张表不会被动到」。</summary>
    public string CustomerFormHint => _editingCustomerId is null
        ? "只有名称必填，其余留空也能创建；保存后立刻写入本地缓存并出现在列表顶部。"
        : "只修改基础资料；人员记录、核对记录、文件与产出不受影响。";

    /// <summary>删除确认浮层是否展开。</summary>
    public bool IsDeleteConfirmOpen
    {
        get => _isDeleteConfirmOpen;
        private set => Set(ref _isDeleteConfirmOpen, value);
    }

    /// <summary>确认框里显示的客户名（让用户看清自己要删的是哪一个）。</summary>
    public string DeleteTargetName
    {
        get => _deleteTargetName;
        private set => Set(ref _deleteTargetName, value);
    }

    /// <summary>待办表单是否展开。</summary>
    public bool IsTaskFormOpen
    {
        get => _isTaskFormOpen;
        private set => Set(ref _isTaskFormOpen, value);
    }

    // ---- 新建客户表单的字段：全部双向绑定，命名统一 Form* ----
    public string FormName { get => _formName; set => Set(ref _formName, value); }
    public string FormShort { get => _formShort; set => Set(ref _formShort, value); }
    public string FormCode { get => _formCode; set => Set(ref _formCode, value); }
    public string FormType { get => _formType; set => Set(ref _formType, value); }
    public string FormOwner { get => _formOwner; set => Set(ref _formOwner, value); }
    public string FormCity { get => _formCity; set => Set(ref _formCity, value); }
    public string FormContact { get => _formContact; set => Set(ref _formContact, value); }
    public string FormService { get => _formService; set => Set(ref _formService, value); }
    public string FormTax { get => _formTax; set => Set(ref _formTax, value); }
    public string FormPeople { get => _formPeople; set => Set(ref _formPeople, value); }
    public string FormPending { get => _formPending; set => Set(ref _formPending, value); }
    public string FormState { get => _formState; set => Set(ref _formState, value); }

    /// <summary>表单内的行内错误提示。空串表示没有错误（对应的 TextBlock 随之隐藏）。</summary>
    public string FormError
    {
        get => _formError;
        private set
        {
            if (!Set(ref _formError, value)) return;
            OnPropertyChanged(nameof(HasFormError));
        }
    }

    public bool HasFormError => _formError.Length > 0;

    // ---- 新建待办表单的字段 ----
    public string TaskFormTitle { get => _taskFormTitle; set => Set(ref _taskFormTitle, value); }
    public string TaskFormKind { get => _taskFormKind; set => Set(ref _taskFormKind, value); }
    public string TaskFormCustomer { get => _taskFormCustomer; set => Set(ref _taskFormCustomer, value); }
    public string TaskFormDue { get => _taskFormDue; set => Set(ref _taskFormDue, value); }
    public string TaskFormNote { get => _taskFormNote; set => Set(ref _taskFormNote, value); }

    public string TaskFormError
    {
        get => _taskFormError;
        private set
        {
            if (!Set(ref _taskFormError, value)) return;
            OnPropertyChanged(nameof(HasTaskFormError));
        }
    }

    public bool HasTaskFormError => _taskFormError.Length > 0;

    // ---- 新增指标（KPI 页内联一行，不做浮层）----
    public string MetricFormTitle { get => _metricFormTitle; set => Set(ref _metricFormTitle, value); }
    public string MetricFormValue { get => _metricFormValue; set => Set(ref _metricFormValue, value); }
    public string MetricFormNote { get => _metricFormNote; set => Set(ref _metricFormNote, value); }

    public string MetricFormError
    {
        get => _metricFormError;
        private set
        {
            if (!Set(ref _metricFormError, value)) return;
            OnPropertyChanged(nameof(HasMetricFormError));
        }
    }

    public bool HasMetricFormError => _metricFormError.Length > 0;

    // ---- 下拉选项：都是静态数组，XAML 直接 ItemsSource 绑 ----
    public string[] CustomerTypes { get; } = { "劳务派遣", "业务外包", "人事代理" };
    public string[] CustomerStates { get; } = { "待复核", "已完成", "待补材料", "暂无核对" };
    public string[] TaskKinds { get; } = { "审批", "核对", "资料", "协作" };
    public string[] TaskDues { get; } = { "今日", "明日", "本周", "本月" };

    /// <summary>新建待办时的客户下拉项：首项固定是「不关联客户」，其余来自已有客户。</summary>
    // 用字符串而不是 CustomerItem 做选项：任务只存客户名称（TaskCache.Customer 是 string），
    // 直接把名称列出来，保存时不用再做对象→字符串的转换，也不会因为换客户实例而绑丢。
    public ObservableCollection<string> CustomerOptions { get; } = new();

    private void RefreshCustomerOptions()
    {
        var current = TaskFormCustomer;
        CustomerOptions.Clear();
        CustomerOptions.Add(NoCustomerOption);
        foreach (var c in _allCustomers)
        {
            // 客户名可能重复（不同分公司同名），下拉里允许重复，保存时按名称原样写入。
            if (!string.IsNullOrWhiteSpace(c.Name)) CustomerOptions.Add(c.Name);
        }
        // 重新灌列表会把 ComboBox 的选中项顶掉，这里把原选择还原；
        // 原选择已经不在列表里（比如客户被删）就退回第一项，避免 ComboBox 出现空白。
        TaskFormCustomer = CustomerOptions.Contains(current) ? current : NoCustomerOption;
    }

    // 调用方：客户页右上角「＋ 新建客户」。
    // 做完之后：_editingCustomerId 清空（表单进入新建态）、字段重置、编号按现有数据自动递增，浮层展开。
    private void OpenCustomerForm()
    {
        SetEditingTarget(null);
        FormName = string.Empty;
        FormShort = string.Empty;
        FormCode = NextCustomerCode();
        FormType = CustomerTypes[0];
        // 新建表单的负责人预填当前登录账号；账号为空时 SetOperatorName 已回落「演示账号」。
        FormOwner = OperatorName;
        FormCity = string.Empty;
        FormContact = string.Empty;
        FormService = string.Empty;
        FormTax = "已登记";
        FormPeople = "0";
        FormPending = "0";
        FormState = CustomerStates[0];
        FormError = string.Empty;
        IsCustomerFormOpen = true;
    }

    // 调用方：客户详情页「编辑」按钮（EditCustomerCommand）。
    // 做完之后：同一张浮层以编辑态打开，字段是这条客户的现值（编号也带出来，允许改但会查重）。
    private void OpenEditCustomerForm(CustomerItem customer)
    {
        SetEditingTarget(customer.Id);
        FormName = customer.Name;
        FormShort = customer.Short;
        FormCode = customer.Code;
        FormType = customer.Type;
        FormOwner = customer.Owner;
        FormCity = customer.City;
        FormContact = customer.Contact;
        FormService = customer.Service;
        FormTax = customer.Tax;
        FormPeople = customer.People.ToString();
        FormPending = customer.Pending.ToString();
        FormState = customer.State;
        FormError = string.Empty;
        IsCustomerFormOpen = true;
    }

    // 切换「新建 / 编辑」时同步更新标题与副标题。这两个是计算属性，不手发通知就不会刷新。
    private void SetEditingTarget(string? id)
    {
        _editingCustomerId = id;
        OnPropertyChanged(nameof(CustomerFormTitle));
        OnPropertyChanged(nameof(CustomerFormHint));
    }

    private void CloseCustomerForm()
    {
        IsCustomerFormOpen = false;
        FormError = string.Empty;
        SetEditingTarget(null);
    }

    // 调用方：「保存并写入本地」按钮（SaveCustomerCommand），新建与编辑共用。
    // 前提：表单已展开；名称可能为空（会被拦下）。
    // 做完之后：新建 → _allCustomers 首位多一条；编辑 → 原位替换该 Id 的条目（详情表原样保留）。
    //           两种情况都写盘、刷列表、关表单。
    private void SaveCustomer()
    {
        // 步骤 1：必填校验。只校验名称 —— 其余字段留空是有意义的（界面显示空白而不是「未填写」），
        // 强行要求全部填满只会让用户为了点保存而胡乱输入。
        var name = FormName.Trim();
        if (name.Length == 0)
        {
            FormError = "客户名称必填。";
            return;
        }

        // 步骤 2：编号。留空就自动生成 KH-001 这种；填了就要查重，否则列表里会出现两行同编号，
        // 后续按编号定位（客户详情深链、文件命名）都会指错。
        // 编辑态必须把「自己」排除在查重之外，否则原样保存都会报「编号已存在」。
        var code = FormCode.Trim();
        if (code.Length == 0) code = NextCustomerCode();
        if (_allCustomers.Any(c =>
                c.Id != _editingCustomerId &&
                string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)))
        {
            FormError = $"编号 {code} 已存在，换一个或留空自动生成。";
            return;
        }

        // 步骤 3：数字字段。空串当 0，写了非数字才报错 —— 这是「空表单直接保存」也能成功的唯一前提。
        if (!TryParseCount(FormPeople, out var people) || !TryParseCount(FormPending, out var pending))
        {
            FormError = "在册人数与待处理数请填 0 或正整数。";
            return;
        }

        var now = DateTime.Now;
        var owner = FormOwner.Trim();

        // 步骤 4a：编辑 —— 按 Id 找回原条目，只替换基础字段，详情四张表整体带过去。
        if (_editingCustomerId is not null)
        {
            var index = _allCustomers.FindIndex(c => c.Id == _editingCustomerId);
            if (index < 0)
            {
                // 极端情况：打开表单期间这条客户被别处删掉了（同一进程内只有这里会删）。
                FormError = "这条客户已不存在，请关闭表单后刷新列表。";
                return;
            }

            var old = _allCustomers[index];
            // 最近更新要补一条「改过什么、什么时候改的」，插到最前面。
            // Activities 在 CustomerItem 上是 IReadOnlyList，不能就地 Insert，先建新列表再拼旧数据。
            var activities = new List<ActivityItem>
            {
                new() { Text = "更新客户档案", Time = $"{now:MM-dd HH:mm} · {owner}" }
            };
            activities.AddRange(old.Activities);
            var updated = new CustomerItem
            {
                Id = old.Id,
                Name = name,
                Short = FormShort.Trim().Length > 0 ? FormShort.Trim() : name,
                Code = code,
                Type = FormType,
                Owner = owner,
                City = FormCity.Trim(),
                Contact = FormContact.Trim(),
                Service = FormService.Trim(),
                Tax = FormTax.Trim(),
                // 文件名不在这张表单里，原样保留；其余基础字段用新值。
                File = old.File,
                Date = now.ToString("MM-dd HH:mm"),
                State = FormState,
                People = people,
                Pending = pending,
                OpenCommand = OpenCustomerCommand,
                // 四个详情表直接复用原实例的列表（只读引用，不深拷贝）：
                // 表单改不到它们，克隆反而会让「界面上的行」和「旧对象里的行」变成两份。
                Activities = activities,
                Staff = old.Staff,
                Runs = old.Runs,
                Files = old.Files
            };

            _allCustomers[index] = updated;
            RefreshCustomers();
            RefreshCustomerOptions();
            SaveWorkspace();
            // 编辑的是当前正在看的那条 → 重新进详情，标题与基础资料立刻换成新值。
            // 这里必须重建 Current（CustomerItem 的字段是 init-only，改不了原对象）。
            var reopen = _current is not null && _current.Id == updated.Id;
            CloseCustomerForm();
            if (reopen) OpenCustomer(updated, _detailTab);
            return;
        }

        // 步骤 4b：新建 —— 建对象、插到最前面（列表按创建时间倒序更符合直觉）。
        var item = new CustomerItem
        {
            Id = NewId("c"),
            Name = name,
            Short = FormShort.Trim().Length > 0 ? FormShort.Trim() : name,
            Code = code,
            Type = FormType,
            Owner = owner,
            City = FormCity.Trim(),
            Contact = FormContact.Trim(),
            Service = FormService.Trim(),
            Tax = FormTax.Trim(),
            File = string.Empty,
            Date = now.ToString("MM-dd HH:mm"),
            State = FormState,
            People = people,
            Pending = pending,
            OpenCommand = OpenCustomerCommand,
            // 详情页四个页签先给空表：新建时还没有人员、核对与文件。
            // 最近更新补一条「新建客户档案」，让详情页不是完全空白，也留下创建痕迹。
            Activities = new List<ActivityItem>
            {
                new() { Text = "新建客户档案", Time = $"{now:MM-dd HH:mm} · {owner}" }
            }
        };

        // 步骤 5：刷新 + 落盘 + 关表单。
        _allCustomers.Insert(0, item);
        RefreshCustomers();
        RefreshCustomerOptions();
        SaveWorkspace();
        CloseCustomerForm();
    }

    // ===================== 删除客户 =====================
    // 删除是唯一不可撤销的动作，所以走「确认浮层」而不是二次点击同一个按钮：
    // 浮层里会把客户名念一遍，用户能确认自己删的不是同名的另一个。

    // 调用方：详情页「删除」按钮（DeleteCustomerCommand）。
    private void AskDeleteCustomer(CustomerItem customer)
    {
        _deleteTargetId = customer.Id;
        DeleteTargetName = customer.Name;
        IsDeleteConfirmOpen = true;
    }

    // 调用方：确认框「确认删除」（ConfirmDeleteCommand）。
    // 做完之后：_allCustomers 少一条、写盘、刷列表；如果删的正是当前详情页那条，退回列表页。
    private void ConfirmDelete()
    {
        var index = _allCustomers.FindIndex(c => c.Id == _deleteTargetId);
        // 找不到就当作已删（比如连点两次确认）：照样关浮层，不报错。
        if (index >= 0) _allCustomers.RemoveAt(index);

        // 删的是正在看的那条 → 先把 Current 置空再回列表，否则详情页会用着已移除的实例。
        var deletedCurrent = _current is not null && _current.Id == _deleteTargetId;
        if (deletedCurrent) Current = null;

        RefreshCustomers();
        RefreshCustomerOptions();
        SaveWorkspace();
        CloseDeleteConfirm();

        if (deletedCurrent) Go(AdminPage.Customers);
    }

    private void CloseDeleteConfirm()
    {
        IsDeleteConfirmOpen = false;
        _deleteTargetId = string.Empty;
        DeleteTargetName = string.Empty;
    }

    // 调用方：审批页右上角「＋ 新建待办」。
    private void OpenTaskForm()
    {
        TaskFormTitle = string.Empty;
        TaskFormKind = TaskKinds[0];
        TaskFormDue = TaskDues[0];
        TaskFormNote = string.Empty;
        TaskFormError = string.Empty;
        // 客户下拉每次打开都重建一次：期间可能在别的页面刚建过客户。
        RefreshCustomerOptions();
        IsTaskFormOpen = true;
    }

    private void CloseTaskForm()
    {
        IsTaskFormOpen = false;
        TaskFormError = string.Empty;
    }

    // 调用方：「保存待办」按钮（SaveTaskCommand）。
    // 做完之后：_allTasks 首位多一条（状态固定「待处理」）、切回「待处理」筛选、写盘、关表单。
    private void SaveTask()
    {
        var title = TaskFormTitle.Trim();
        if (title.Length == 0)
        {
            TaskFormError = "待办标题必填。";
            return;
        }

        var item = new TaskItem
        {
            Id = NewId("t"),
            Title = title,
            Kind = TaskFormKind,
            // 「不关联客户」是界面占位，落盘时存空串：缓存里的 Customer 字段没有「占位」这个概念。
            CustomerName = TaskFormCustomer == NoCustomerOption ? string.Empty : TaskFormCustomer,
            Due = TaskFormDue,
            Status = "待处理",
            Note = TaskFormNote.Trim(),
            // 命令按实例注入，和 LoadWorkspace 里读到旧任务时保持一致（DataTemplate 靠条目自身拿命令）。
            ApproveCommand = ApproveCommand,
            RejectCommand = RejectCommand
        };

        _allTasks.Insert(0, item);
        // 新建的待办一定是「待处理」，如果当前停在「全部记录」筛选，切回去才能让用户看到它。
        TaskFilter = "待处理";
        RefreshTasks();
        SaveWorkspace();
        CloseTaskForm();
    }

    // 调用方：KPI 页底部「新增指标」按钮（AddMetricCommand）。
    // 做完之后：Metrics 末尾多一行、写盘、输入框清空；页面不跳转。
    private void AddMetric()
    {
        var title = MetricFormTitle.Trim();
        if (title.Length == 0)
        {
            MetricFormError = "指标名称必填。";
            return;
        }

        Metrics.Add(new MetricRow
        {
            Title = title,
            Value = MetricFormValue.Trim(),
            Note = MetricFormNote.Trim()
        });
        // HasMetrics 是计算属性，加完必须手动通知，否则空态提示不会消失。
        OnPropertyChanged(nameof(HasMetrics));
        SaveWorkspace();
        MetricFormTitle = string.Empty;
        MetricFormValue = string.Empty;
        MetricFormNote = string.Empty;
        MetricFormError = string.Empty;
    }

    // ---- 小工具 ----

    /// <summary>空串/纯空白视为 0；非数字或负数返回 false（由调用方转成提示文案）。</summary>
    private static bool TryParseCount(string text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return true;
        return int.TryParse(text.Trim(), out value) && value >= 0;
    }

    /// <summary>生成 KH-001 形式的客户编号：取现有编号里的最大数字 +1，三位补零。</summary>
    private string NextCustomerCode()
    {
        var max = 0;
        foreach (var c in _allCustomers)
        {
            var code = c.Code ?? string.Empty;
            var dash = code.LastIndexOf('-');
            if (dash >= 0 && int.TryParse(code[(dash + 1)..], out var n) && n > max) max = n;
        }
        return $"KH-{max + 1:000}";
    }

    /// <summary>内部 Id：不需要跨机器唯一，只要在同一份缓存里不重复即可，取 Guid 前 8 位够用。</summary>
    private static string NewId(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8];

    // ===================== 本地缓存（唯一数据源） =====================
    // v2 起工作区不再有硬编码演示数据：构造函数里的 LoadWorkspace 把缓存读成内存集合，
    // 之后每一处会改变数据的动作（发消息 / 审批）都调一次 SaveWorkspace 整体写回。
    //
    // 两个方法的职责边界：
    //   LoadWorkspace —— 只在构造函数里跑一次，只读缓存、只写内存；
    //   SaveWorkspace —— 只读内存、只写缓存，不碰页面状态。
    // 之所以整体写回而不是分块写：文件本来就小（几十 KB），一次原子写比维护四份局部写更不容易出错；
    // 真正的性能保护交给 LocalCache.SaveSoon 的防抖。

    /// <summary>
    /// 把本地缓存里的工作区数据读进内存集合：客户（含四个详情页签）、待办、KPI 指标、对话记录。
    /// </summary>
    // 调用点：构造函数步骤 3（集合初始化之前）。
    // 前提：_allCustomers / _allTasks / Metrics / Messages 四个容器已存在。
    // 做完之后：四个容器里是缓存里的原样数据；缓存为空时它们也是空的，界面走各自的空态。
    //
    // 三条容错（都在这里做，读取时不必再判）：
    //   · 缓存文件里缺某个数组 → Normalize 已补成空列表；
    //   · 客户缺 activities/staff/runs/files → 详情页对应页签为空，不抛异常；
    //   · 待办缺 status → 用 TaskCache 的默认值「待处理」，所以未填状态的任务照样能出现在默认筛选里。
    private void LoadWorkspace()
    {
        var ws = LocalCache.Data.Workspace;

        _allCustomers.Clear();
        foreach (var c in ws.Customers)
            _allCustomers.Add(ToCustomerItem(c));

        _allTasks.Clear();
        foreach (var t in ws.Tasks)
            _allTasks.Add(new TaskItem
            {
                Id = t.Id,
                Title = t.Title,
                Kind = t.Kind,
                CustomerName = t.Customer,
                Due = t.Due,
                Status = t.Status,
                Note = t.Note,
                // 审批 / 退回命令按实例注入：按钮写在 DataTemplate 里，只能从条目自身拿到命令。
                ApproveCommand = ApproveCommand,
                RejectCommand = RejectCommand
            });

        Metrics.Clear();
        foreach (var m in ws.Metrics)
            Metrics.Add(new MetricRow { Title = m.Title, Value = m.Value, Note = m.Note });

        Messages.Clear();
        foreach (var m in ws.Messages)
            Messages.Add(new ChatMessage { Author = m.Author, Text = m.Text, IsUser = m.IsUser });
    }

    /// <summary>缓存模型 → 界面模型（客户）。详情四张表一并转换，供 FillDetail 直接复制。</summary>
    private CustomerItem ToCustomerItem(CustomerCache c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Short = c.Short,
        Code = c.Code,
        Type = c.Type,
        Owner = c.Owner,
        City = c.City,
        Contact = c.Contact,
        Service = c.Service,
        Tax = c.Tax,
        File = c.File,
        Date = c.Date,
        State = c.State,
        People = c.People,
        Pending = c.Pending,
        OpenCommand = OpenCustomerCommand,
        Activities = c.Activities.Select(a => new ActivityItem { Text = a.Text, Time = a.Time }).ToList(),
        Staff = c.Staff.Select(p => new PersonRow
        {
            No = p.No, Name = p.Name, IdInfo = p.IdInfo,
            Customer = p.Customer, Status = p.Status, Source = p.Source
        }).ToList(),
        Runs = c.Runs.Select(r => new RunRow
        {
            Batch = r.Batch, Period = r.Period, Version = r.Version,
            Status = r.Status, Pending = r.Pending, Date = r.Date
        }).ToList(),
        Files = c.Files.Select(f => new FileRow
        {
            Name = f.Name, Purpose = f.Purpose, Related = f.Related,
            Time = f.Time, Tone = f.Tone
        }).ToList()
    };

    /// <summary>把内存里的工作区数据整体写回缓存（延迟合并写）。</summary>
    // 调用点：Send（用户消息之后、Agent 回复之后）、Decide（审批之后）。
    // 取的是 _allCustomers / _allTasks 这两个「全量源列表」，不是 Customers / Tasks 这两个过滤结果 ——
    // 否则在「有待处理事项」筛选下保存会把不满足筛选条件的客户从文件里抹掉。
    private void SaveWorkspace()
    {
        var ws = LocalCache.Data.Workspace;

        ws.Customers.Clear();
        foreach (var c in _allCustomers)
            ws.Customers.Add(new CustomerCache
            {
                Id = c.Id, Name = c.Name, Short = c.Short, Code = c.Code,
                Type = c.Type, Owner = c.Owner, City = c.City, Contact = c.Contact,
                Service = c.Service, Tax = c.Tax, File = c.File, Date = c.Date,
                State = c.State, People = c.People, Pending = c.Pending,
                // 详情四张表原样回写：它们本来就是从缓存读进来的，界面不改这些行，
                // 但整体写回能保证文件与内存永远一致（手改过的字段也不会被丢掉）。
                Activities = c.Activities.Select(a => new ActivityCache { Text = a.Text, Time = a.Time }).ToList(),
                Staff = c.Staff.Select(p => new StaffCache
                {
                    No = p.No, Name = p.Name, IdInfo = p.IdInfo,
                    Customer = p.Customer, Status = p.Status, Source = p.Source
                }).ToList(),
                Runs = c.Runs.Select(r => new RunCache
                {
                    Batch = r.Batch, Period = r.Period, Version = r.Version,
                    Status = r.Status, Pending = r.Pending, Date = r.Date
                }).ToList(),
                Files = c.Files.Select(f => new FileEntryCache
                {
                    Name = f.Name, Purpose = f.Purpose, Related = f.Related,
                    Time = f.Time, Tone = f.Tone
                }).ToList()
            });

        ws.Tasks.Clear();
        foreach (var t in _allTasks)
            ws.Tasks.Add(new TaskCache
            {
                Id = t.Id, Title = t.Title, Kind = t.Kind,
                Customer = t.CustomerName, Due = t.Due,
                Status = t.Status, Note = t.Note
            });

        ws.Metrics.Clear();
        foreach (var m in Metrics)
            ws.Metrics.Add(new MetricCache { Title = m.Title, Value = m.Value, Note = m.Note });

        ws.Messages.Clear();
        foreach (var m in Messages)
        {
            // 两类消息不落盘：
            //   · 错误提示（未配置 Key、网络失败）是瞬时反馈；
            //   · 确认卡片是一次性的交互态，重启后按钮对应的 PendingWrite 已经不存在，
            //     落盘只会留下点不动的死卡片。
            if (m.IsError || m.IsConfirmCard) continue;
            ws.Messages.Add(new MessageCache { Author = m.Author, Text = m.Text, IsUser = m.IsUser });
        }

        LocalCache.SaveSoon();

        // 数据已经写进 LocalCache.Data，再通知壳层重建左侧导航：
        // 客户新建 / 编辑 / 删除、发消息、审批都能在这里一次覆盖。
        // 放最后是为了让订阅者读到的 AllCustomers / HasMessages 已经是新值。
        WorkspaceChanged?.Invoke(this, EventArgs.Empty);
    }
}

// ── 关于「演示数据」────────────────────────────────────────────────────────
// v1 里这里还有 BuildCustomers / BuildTasks 两个方法，硬编码了 4 个客户与 7 条待办。
// v2 起这些内容全部搬进本地缓存文件（workspace.customers / workspace.tasks），
// 代码只负责「读出来画」，不再提供任何兜底演示数据 —— 也就是说：
//   · 缓存文件为空 → 界面就是空的（各页有各自的空态提示）；
//   · 想造数据 → 直接编辑 local-cache.json，重启即生效；
//   · 想回到初始状态 → 删掉该文件（或设置页「清理」），程序会重建一份空的。
