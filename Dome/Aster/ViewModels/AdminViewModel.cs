// ============================================================================
// AdminViewModel.cs —— 工作区（工作台 / 协作与审批 / 客户工作空间 / Workflow / KPI）
// 结构参考 HR Agent 交互原型：侧栏四个入口 + 客户列表 + 客户详情四页签 + 三步核对。
// 数据全部是内置演示数据，不连后端；页面切换用 AdminPage 枚举 + 一组 bool 供 XAML 绑 IsVisible。
// 本地持久化：工作区的全部展示数据（客户及其详情、待办、KPI、对话）都来自
// Services.LocalCache 的 workspace 段，代码里没有任何硬编码演示数据；
// 启动时 LoadWorkspace 读出来画，发消息 / 审批之后 SaveWorkspace 整体写回。
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
using System.Linq;
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

// 四个属性都是 init-only：实例一旦创建就不可变。所以「发消息」只能往集合里 Add 新对象，
// 不能改已有消息的 Text —— 后者即使改了也不会发 PropertyChanged，界面不会动。
// IsAgent 是 IsUser 的取反，XAML 靠这一对布尔把同一条消息渲染成「左侧无气泡」或「右侧蓝气泡」。
/// <summary>对话消息。</summary>
public sealed class ChatMessage
{
    public string Author { get; init; } = "Agent";
    public string Text { get; init; } = string.Empty;
    public bool IsUser { get; init; }
    public bool IsAgent => !IsUser;
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
        // 快捷卡命令：三张卡共用一个命令，靠 Title 字符串分派。
        // 「新建项目」没有独立页面，落到 default 的客户工作空间；另外两张各去一页。
        // 用文案当分派键是演示代码的取巧之处：改 Cards 里的 Title 会静默改变点击行为。
        CardCommand = new RelayCommand(o =>
        {
            if (o is not WorkbenchCard card) return;
            switch (card.Title)
            {
                case "KPI 考勤": Go(AdminPage.Kpi); break;
                case "待办事项": Go(AdminPage.Approval); break;
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
        SendCommand = new RelayCommand(Send);
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
            new() { Chip = "创建", Tone = "accent", Title = "新建项目", Desc = "创建客户或项目工作空间。", Command = CardCommand },
            new() { Chip = "查看", Tone = "green", Title = "KPI 考勤", Desc = "查看绩效、考勤异常、响应时效。", Command = CardCommand },
            new() { Chip = "查看", Tone = "red", Title = "待办事项", Desc = "审批、核对、资料更新与协作请求。", Command = CardCommand }
        };

        // 对话从空集合起步：缓存里有几条就画几条，没有再冷启动问候语 ——
        // 「界面内容全部来自本地文件」这条原则不给自己留例外。
        Messages = new ObservableCollection<ChatMessage>();

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
    // key 的三种语义：
    // 1) 页面 key：approval / clients / customers / workflow / kpi，直接 Go。
    //    clients 与 customers 是同一个页面的别名（rail 用 clients，内部习惯用 customers）。
    // 2) 详情 key：customer-qinghe / detail / detail-people / detail-runs / detail-files，
    //    都是「打开第一个客户 + 指定页签」；demo 只有一个项目条目，所以全部指向青禾。
    // 3) 其余（recent-1..3 / recent-tax / thread-new / workbench）落到 default → 工作台。
    //    「最近会话」是壳层的占位条目，没有独立页面，点它等价于回工作台：
    //    这是有意的降级，不是漏写分支。
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
            // 二级导航的「项目」条目：打开第一个客户的详情首页。
            case "customer-qinghe": OpenFirstCustomer("base", hasFirst); break;
            // 「8 月个税交叉验证」这条会话：打开同一个客户，但直接落在核对记录页签。
            case "thread-tax": OpenFirstCustomer("runs", hasFirst); break;
            // 下面四个 detail-* 是详情页内部深链；detail 与 customer-qinghe 等价。
            // 它们不做「当前客户」判断，永远打开第一个客户，多客户时需要改成带 id 的 key。
            case "detail": OpenFirstCustomer("base", hasFirst); break;
            case "detail-people": OpenFirstCustomer("people", hasFirst); break;
            case "detail-runs": OpenFirstCustomer("runs", hasFirst); break;
            case "detail-files": OpenFirstCustomer("files", hasFirst); break;
            // 兜底：recent-* / thread-new / workbench 以及任何拼错的 key 都回工作台。
            // 兜底而不抛异常，是为了让壳层新增占位条目时不会把演示点崩。
            default: Go(AdminPage.Workbench); break;
        }
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
    public ICommand SendCommand { get; }

    public string Draft
    {
        get => _draft;
        // 与 Composer 的 TextBox 双向绑定。code-behind 的 Enter 处理只负责触发 SendCommand，
        // 不直接读 TextBox，所以草稿永远以这个属性为准。Set 在值相等时提前返回，
        // 避免每敲一个相同的字符都发一次通知。
        set => Set(ref _draft, value);
    }

    // 调用方：输入卡右侧的发送按钮（SendCommand）与 code-behind 的 OnComposerKeyDown（Enter）。
    // 前提：不要求当前页面是工作台 —— 命令不做页面判断；Draft 可能为空。
    // 做完之后：Messages 末尾多两条（用户一条、稍后 Agent 一条），Draft 被清空。
    private void Send()
    {
        // 步骤 1：取快照并 Trim。空串/纯空白直接 return：既不发消息也不清空草稿，
        // 因为用户可能只是误触 Enter，清空会让他丢掉正在写的半句话。
        var text = Draft?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        // 步骤 2：追加用户消息。集合 Add 会发 CollectionChanged，工作台的消息 ItemsControl
        // 自动刷新；code-behind 也订阅了同一事件，用来把 ChatScroll 滚到底。
        Messages.Add(new ChatMessage { Author = "张客服", Text = text, IsUser = true });
        // 步骤 3：清空草稿，TextBox 由双向绑定立刻变空。
        Draft = string.Empty;
        // 步骤 3.5：把工作区写回本地缓存。用户在输入框里说的话是唯一「不可再生的数据」，
        // 丢了就真没了；Agent 的回复是规则文案、丢了也无所谓，但为了保持顺序一致一起存。
        // 用 SaveSoon 而不是 Save：下面 600ms 后还会追加一条 Agent 回复，两次写会被合并成一次。
        SaveWorkspace();

        // 步骤 4：起一个一次性 DispatcherTimer 模拟「Agent 正在输入」。600ms 是演示用的假延迟，
        // 不是为了等任何真实计算；期间界面完全可操作，没有加载态、也没有禁用发送按钮。
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += (_, _) =>
        {
            // 先 Stop：DispatcherTimer 不会自己停，不 Stop 就会按 600ms 反复追加同一条回复。
            timer.Stop();
            // 步骤 5：回调闭包捕获的是 this.Messages 字段，而不是某个局部变量或当前页面快照。
            // 由此带出两个副作用：a) 600ms 内切到别的页面，这条回复仍会进工作台集合，
            // 用户回到工作台才看得到；b) 连按几次发送会起多个独立定时器，各自回一条，没有防重入。
            Messages.Add(new ChatMessage
            {
                Author = "Agent",
                Text = "当前是规则式演示：输入「新建客户」「核对个税」「查看待办」「审批」或「修改资料」可以打开对应面板。"
            });
            // 步骤 6：补一条落盘。前面 SaveSoon 起的 700ms 防抖还没到期，
            // 这里再调一次只是把计时重置，最终只有一次写盘 —— 但少了这一句，
            // 用户「发完消息立刻关掉程序」时这条回复可能来不及存。
            SaveWorkspace();
        };
        timer.Start();
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
            && (_filter != "我负责的客户" || c.Owner == "张客服"));

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

    // 向导顶部「关联客户」显示的名字。Current 在构造函数末尾必不为 null，
    // 所以 ?? 的右侧实际走不到，只是给「将来允许未选客户直接进 Workflow」留的兜底。
    public string WfCustomerName => Current?.Name ?? "青禾新能源有限公司";
    public string WfCustomerCode => Current?.Code ?? "KH-001";

    // 调用方：「开始样例核对」按钮（WfStartCommand）。
    // 前提：WfAccepted 必须为 true —— 这是「不读取真实附件」的免责勾选，没勾就不让跑。
    // 做完之后：生成 3 条样例结果、清空错误、WfStep 从 0 推到 1，面板由第一步切到第二步。
    private void StartRun()
    {
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

    // 指标已经不在这里构造了：内容来自本地缓存的 workspace.metrics（见 LoadWorkspace / SaveWorkspace）。
    // 想加一行指标就改文件，不加就一行都不显示 —— 代码里没有兜底的三条演示值。

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
            ws.Messages.Add(new MessageCache { Author = m.Author, Text = m.Text, IsUser = m.IsUser });

        LocalCache.SaveSoon();
    }
}

// ── 关于「演示数据」────────────────────────────────────────────────────────
// v1 里这里还有 BuildCustomers / BuildTasks 两个方法，硬编码了 4 个客户与 7 条待办。
// v2 起这些内容全部搬进本地缓存文件（workspace.customers / workspace.tasks），
// 代码只负责「读出来画」，不再提供任何兜底演示数据 —— 也就是说：
//   · 缓存文件为空 → 界面就是空的（各页有各自的空态提示）；
//   · 想造数据 → 直接编辑 local-cache.json，重启即生效；
//   · 想回到初始状态 → 删掉该文件（或设置页「清理」），程序会重建一份空的。
