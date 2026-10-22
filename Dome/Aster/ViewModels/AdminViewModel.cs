// ============================================================================
// AdminViewModel.cs —— 工作区（工作台 / 协作与审批 / 客户工作空间 / Workflow / KPI）
// 结构参考 HR Agent 交互原型：侧栏四个入口 + 客户列表 + 客户详情四页签 + 三步核对。
// 数据全部是内置演示数据，不连后端；页面切换用 AdminPage 枚举 + 一组 bool 供 XAML 绑 IsVisible。
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
// OpenCommand 由 BuildCustomers 注入 OpenCustomerCommand，XAML 再用 CommandParameter="{Binding}"
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
// 3) _allCustomers / _allTasks 两个私有源列表：过滤只重建对外的 ObservableCollection，
//    源列表始终保持全量，所以清空搜索词能立刻恢复全部数据。
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
    // 步骤 3：BuildCustomers / BuildTasks / BuildMetrics 往私有源列表灌演示数据。
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
            // Chip 里的「3 项」「12 条」是写死的演示文案，不随后续审批结果变化；
            // 真实待处理数要看 PendingCount（种子数据是 7 条），两者对不上属于演示数据的不一致。
            new() { Chip = "创建", Tone = "accent", Title = "新建项目", Desc = "创建客户或项目工作空间。", Command = CardCommand },
            new() { Chip = "3 项", Tone = "green", Title = "KPI 考勤", Desc = "查看绩效、考勤异常、响应时效。", Command = CardCommand },
            new() { Chip = "12 条", Tone = "red", Title = "待办事项", Desc = "审批、核对、资料更新与协作请求。", Command = CardCommand }
        };

        Messages = new ObservableCollection<ChatMessage>
        {
            new() { Author = "Agent", Text = "早上好。今天有 12 条待办，其中 3 条需要审批。你可以创建客户项目，或开始一次交叉验证。" },
            new() { Author = "张客服", IsUser = true, Text = "先帮我核对青禾新能源的 8 月个税，再把结果保存到客户工作空间。" },
            new() { Author = "Agent", Text = "可以。我会先确认客户与材料，再展示样例差异，复核后归档。" }
        };

        BuildCustomers();
        BuildTasks();
        BuildMetrics();

        Customers = new ObservableCollection<CustomerItem>(_allCustomers);
        Tasks = new ObservableCollection<TaskItem>();
        PeopleRows = new ObservableCollection<PersonRow>();
        RunRows = new ObservableCollection<RunRow>();
        FileRows = new ObservableCollection<FileRow>();
        Activities = new ObservableCollection<ActivityItem>();

        RefreshTasks();
        OpenCustomer(_allCustomers[0], "base");
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
        switch (key)
        {
            // 协作与审批：只换页，任务列表由 RefreshTasks 提前准备好，这里不重建。
            case "approval": Go(AdminPage.Approval); break;
            // 客户工作空间的两个别名，落到同一页；保留 customers 是为了兼容内部旧调用。
            case "clients":
            case "customers": Go(AdminPage.Customers); break;
            // Workflow 向导：不重置 WfStep，所以上次停在第几步这次还停在第几步。
            case "workflow": Go(AdminPage.Workflow); break;
            // KPI：纯只读页，数据在构造函数 BuildMetrics 里一次性建好。
            case "kpi": Go(AdminPage.Kpi); break;
            // 二级导航的「项目」条目：打开青禾的详情首页。
            case "customer-qinghe": OpenCustomer(_allCustomers[0], "base"); break;
            // 「8 月个税交叉验证」这条会话：打开同一个客户，但直接落在核对记录页签。
            case "thread-tax": OpenCustomer(_allCustomers[0], "runs"); break;
            // 下面四个 detail-* 是详情页内部深链；detail 与 customer-qinghe 等价。
            // 它们不做「当前客户」判断，永远打开 _allCustomers[0]，多客户时需要改成带 id 的 key。
            case "detail": OpenCustomer(_allCustomers[0], "base"); break;
            case "detail-people": OpenCustomer(_allCustomers[0], "people"); break;
            case "detail-runs": OpenCustomer(_allCustomers[0], "runs"); break;
            case "detail-files": OpenCustomer(_allCustomers[0], "files"); break;
            // 兜底：recent-* / thread-new / workbench 以及任何拼错的 key 都回工作台。
            // 兜底而不抛异常，是为了让壳层新增占位条目时不会把演示点崩。
            default: Go(AdminPage.Workbench); break;
        }
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
    public string CustomerTotal => $"演示数据 · 共 {_allCustomers.Count} 个客户";
    public string[] Filters { get; } = { "全部客户", "有待处理事项", "我负责的客户" };

    // 调用方：Query / Filter 的 setter。前提：_allCustomers 已由 BuildCustomers 填好。
    // 做完之后：Customers 换成过滤结果，CustomerCount 重算并通知；_allCustomers 保持全量不变。
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
        // 步骤 4：CustomerCount 是计算属性，集合变了不会自动通知，必须手发。
        // CustomerTotal 读的是 _allCustomers.Count，与过滤无关，所以不用发。
        OnPropertyChanged(nameof(CustomerCount));
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
    // 做完之后：Activities 固定 3 条；PeopleRows 0 或 3 条；RunRows 0 或 1 条；FileRows 1 或 2 条。
    // 全部是演示数据：人数刻意只造 3 条固定样例，而不是按 c.People 循环，避免 68 人把页面刷满。
    private void FillDetail(CustomerItem c)
    {
        // 步骤 1：最近更新固定三条，时间文案里拼上该客户自己的日期、负责人和人数，
        // 制造「数据与客户相关」的观感。日期格式直接沿用数据里写死的 "10-17 10:32"。
        Activities.Clear();
        Activities.Add(new ActivityItem { Text = "客户资料更新至 V2", Time = $"{c.Date} · {c.Owner} 确认" });
        Activities.Add(new ActivityItem { Text = "保存人员资料", Time = $"当前在册 {c.People} 人，可在人员记录中查阅" });
        Activities.Add(new ActivityItem { Text = "建立客户项目", Time = "10-12 09:20 · 归档初始交接资料" });

        // 步骤 2：People > 0 才造人员行；EMP-00i 与 (char)('A' + i) 是刻意的假编号、假姓名，
        // 证件信息用默认的「已脱敏」，保证演示里不出现真实个人信息。
        PeopleRows.Clear();
        if (c.People > 0)
        {
            for (var i = 0; i < 3; i++)
                PeopleRows.Add(new PersonRow
                {
                    No = $"EMP-00{i + 1}",
                    Name = $"演示员工 {(char)('A' + i)}",
                    Customer = c.Short,
                    Source = "人员资料_v2.xlsx"
                });
        }

        // 步骤 3：State 为「暂无核对」表示该客户从没跑过核对，于是不造 RunRow，
        // 页签里会走「暂无核对记录」的空状态。Batch 用 c.Code[3..] 截掉「KH-」前缀，
        // 拼成 RC-001-01 这种批次号；切片假设编号至少 3 个字符，来自 BuildCustomers 的固定数据。
        RunRows.Clear();
        if (c.State != "暂无核对")
        {
            RunRows.Add(new RunRow
            {
                Batch = $"RC-{c.Code[3..]}-01",
                Period = "2026-08",
                Version = "V2",
                Status = c.State,
                Pending = c.Pending > 0 ? c.Pending.ToString() : "—",
                Date = c.Date
            });
        }
        // 步骤 4：HasRunRows 由 RunRows.Count 推导，集合变动不会自动通知它 ——
        // 这里手动补一条，否则「暂无核对记录」的提示不会随客户切换而显隐。
        OnPropertyChanged(nameof(HasRunRows));

        // 步骤 5：文件永远有原始材料；只有跑过核对才有第二行 Excel 产出，
        // Tone=ok 让用途标签借 StatusPalette 走绿色，和灰色的原始材料区分开。
        FileRows.Clear();
        FileRows.Add(new FileRow { Name = c.File, Purpose = "原始材料", Related = "客户档案 V2", Time = c.Date });
        if (c.State != "暂无核对")
            FileRows.Add(new FileRow
            {
                Name = $"{c.Short}_个税核对_202608_V2.xlsx",
                Purpose = "Excel 产出",
                Related = "个税核对 V2",
                Time = c.Date,
                Tone = "ok"
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

    // 调用方：构造函数、TaskFilterCommand、Decide。前提：_allTasks 已由 BuildTasks 填好。
    // 做完之后：Tasks 换成过滤后的同一批实例（不克隆），并重算 TaskSummary / PendingCount。
    // 因为放的是原对象，审批改了 Status 之后，「全部」视图里同一行会同步变色，不需要重建对象。
    private void RefreshTasks()
    {
        // 「全部」不过滤；其余任何取值都按 IsPending 过滤（当前 UI 只会传「待处理」）。
        var list = _allTasks.Where(t => _taskFilter == "全部" || t.IsPending);
        Tasks.Clear();
        foreach (var t in list) Tasks.Add(t);
        // 两个计算属性都依赖 Tasks.Count 与源列表，集合变了不会自动通知，逐条补发。
        OnPropertyChanged(nameof(TaskSummary));
        OnPropertyChanged(nameof(PendingCount));
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

    // 只在构造函数里调用一次。三条都是固定演示值，不随审批结果变化；
    // 其中「任务处理 0 / 12」的 12 与 Messages 里的欢迎语对齐，但和实际种子任务数（7）并不一致。
    // 方法内没有 Clear，重复调用会把三条叠加成六条。
    private void BuildMetrics()
    {
        Metrics.Add(new MetricRow { Title = "任务处理", Value = "0 / 12", Note = "随演示操作更新" });
        Metrics.Add(new MetricRow { Title = "考勤异常（固定样例）", Value = "2 项", Note = "缺少打卡记录，需人工核实" });
        Metrics.Add(new MetricRow { Title = "平均响应（固定样例）", Value = "12 分钟", Note = "不用于真实考核" });
    }

    // ===================== 演示数据 =====================
    // 以下三个 Build* 只在构造函数里各跑一次，往私有源列表灌固定数据。
    // 之所以不直接填对外集合：过滤会反复重建 Customers，源列表必须是「永不损耗」的备份。
    private void BuildCustomers()
    {
        // 四条客户覆盖四种状态：待复核（有差异）、已完成、待补材料、暂无核对（没有核对记录）。
        // OpenCommand 注入同一个 OpenCustomerCommand，点击时再由 CommandParameter 把整行带回来。
        // Id 目前没被使用，留着是为了将来做带 id 的深链。
        _allCustomers.Add(new CustomerItem
        {
            Id = "c1", Name = "青禾新能源有限公司", Short = "青禾新能源", Code = "KH-001",
            Type = "劳务派遣", People = 68, Owner = "张客服", Pending = 3, Date = "10-17 10:32",
            State = "待复核", City = "郑州", Contact = "客户联系人 A",
            Service = "工资代发、社保服务", Tax = "已登记 · 演示脱敏", File = "客户交接资料_v2.xlsx",
            OpenCommand = OpenCustomerCommand
        });
        _allCustomers.Add(new CustomerItem
        {
            Id = "c2", Name = "澄川医疗服务有限公司", Short = "澄川医疗服务", Code = "KH-002",
            Type = "劳务派遣", People = 92, Owner = "张客服", Pending = 0, Date = "10-16 16:20",
            State = "已完成", City = "洛阳", Contact = "客户联系人 B",
            Service = "工资代发、商保服务", Tax = "已登记 · 演示脱敏", File = "客户基础信息.xlsx",
            OpenCommand = OpenCustomerCommand
        });
        _allCustomers.Add(new CustomerItem
        {
            Id = "c3", Name = "远山工程管理有限公司", Short = "远山工程", Code = "KH-003",
            Type = "业务外包", People = 35, Owner = "周客服", Pending = 2, Date = "10-16 14:05",
            State = "待补材料", City = "郑州", Contact = "客户联系人 C",
            Service = "社保、商保服务", Tax = "待补充", File = "项目交接资料.xlsx",
            OpenCommand = OpenCustomerCommand
        });
        _allCustomers.Add(new CustomerItem
        {
            Id = "c4", Name = "云帆商贸有限公司", Short = "云帆商贸", Code = "KH-004",
            Type = "人事代理", People = 24, Owner = "周客服", Pending = 0, Date = "10-15 11:40",
            State = "暂无核对", City = "新乡", Contact = "客户联系人 D",
            Service = "人事代理", Tax = "待补充", File = "客户信息采集表.xlsx",
            OpenCommand = OpenCustomerCommand
        });
    }

    private void BuildTasks()
    {
        // 元组数组当种子：只写业务会变的部分（标题/类型/客户/期限），
        // Id 与命令在下面循环里统一生成，避免七条重复写样板代码。
        var seed = new (string Title, string Kind, string Customer, string Due)[]
        {
            ("核对结果归档确认", "审批", "青禾新能源有限公司", "今日"),
            ("客户资料变更确认", "审批", "澄川医疗服务有限公司", "今日"),
            ("补收说明确认", "审批", "远山工程管理有限公司", "今日"),
            ("复核本期差异", "核对", "青禾新能源有限公司", "今日"),
            ("复核本期差异", "核对", "远山工程管理有限公司", "今日"),
            ("补充系统导出表", "补材料", "远山工程管理有限公司", "待安排"),
            ("更新客户资料", "资料", "云帆商贸有限公司", "待安排")
        };

        // 七条种子的 Status 都走 TaskItem 的默认值「待处理」，所以构造函数里 RefreshTasks 一跑，
        // 全部条目都会出现在默认的「待处理」筛选下。
        // 审批/退回命令按实例注入：按钮写在 DataTemplate 里，只能从条目自身拿到命令。
        for (var i = 0; i < seed.Length; i++)
            _allTasks.Add(new TaskItem
            {
                Id = $"t{i + 1}",
                Title = seed[i].Title,
                Kind = seed[i].Kind,
                CustomerName = seed[i].Customer,
                Due = seed[i].Due,
                ApproveCommand = ApproveCommand,
                RejectCommand = RejectCommand
            });
    }
}
