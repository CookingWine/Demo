// ============================================================================
// AdminViewModel.cs —— 工作区（工作台 / 协作与审批 / 客户工作空间 / Workflow / KPI）
// 结构参考 HR Agent 交互原型：侧栏四个入口 + 客户列表 + 客户详情四页签 + 三步核对。
// 数据全部是内置演示数据，不连后端；页面切换用 AdminPage 枚举 + 一组 bool 供 XAML 绑 IsVisible。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Aster.ViewModels;

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

/// <summary>对话消息。</summary>
public sealed class ChatMessage
{
    public string Author { get; init; } = "Agent";
    public string Text { get; init; } = string.Empty;
    public bool IsUser { get; init; }
    public bool IsAgent => !IsUser;
}

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

    public AdminViewModel()
    {
        OpenCustomerCommand = new RelayCommand(o =>
        {
            if (o is CustomerItem c) OpenCustomer(c, "base");
        });
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

    private void Go(AdminPage page)
    {
        _page = page;
        OnPropertyChanged(nameof(IsWorkbench));
        OnPropertyChanged(nameof(IsCustomers));
        OnPropertyChanged(nameof(IsDetail));
        OnPropertyChanged(nameof(IsApproval));
        OnPropertyChanged(nameof(IsWorkflow));
        OnPropertyChanged(nameof(IsKpi));
        OnPropertyChanged(nameof(TopBarTitle));
    }

    /// <summary>外壳（一级导航 / 二级会话列表）调用，把侧栏的 key 映射到页面。</summary>
    public void Select(string key)
    {
        switch (key)
        {
            case "approval": Go(AdminPage.Approval); break;
            case "clients":
            case "customers": Go(AdminPage.Customers); break;
            case "workflow": Go(AdminPage.Workflow); break;
            case "kpi": Go(AdminPage.Kpi); break;
            case "customer-qinghe": OpenCustomer(_allCustomers[0], "base"); break;
            case "thread-tax": OpenCustomer(_allCustomers[0], "runs"); break;
            case "detail": OpenCustomer(_allCustomers[0], "base"); break;
            case "detail-people": OpenCustomer(_allCustomers[0], "people"); break;
            case "detail-runs": OpenCustomer(_allCustomers[0], "runs"); break;
            case "detail-files": OpenCustomer(_allCustomers[0], "files"); break;
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
        set => Set(ref _draft, value);
    }

    private void Send()
    {
        var text = Draft?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        Messages.Add(new ChatMessage { Author = "张客服", Text = text, IsUser = true });
        Draft = string.Empty;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
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
        set
        {
            if (!Set(ref _query, value)) return;
            RefreshCustomers();
        }
    }

    public string Filter
    {
        get => _filter;
        set
        {
            if (!Set(ref _filter, value)) return;
            RefreshCustomers();
        }
    }

    public string CustomerCount => $"显示 {Customers.Count} 个客户";
    public string CustomerTotal => $"演示数据 · 共 {_allCustomers.Count} 个客户";
    public string[] Filters { get; } = { "全部客户", "有待处理事项", "我负责的客户" };

    private void RefreshCustomers()
    {
        var q = _query.Trim().ToLowerInvariant();
        var list = _allCustomers.Where(c =>
            (q.Length == 0 || c.Name.ToLowerInvariant().Contains(q) || c.Code.ToLowerInvariant().Contains(q))
            && (_filter != "有待处理事项" || c.Pending > 0)
            && (_filter != "我负责的客户" || c.Owner == "张客服"));

        Customers.Clear();
        foreach (var c in list) Customers.Add(c);
        OnPropertyChanged(nameof(CustomerCount));
    }

    // ===================== 客户详情 =====================
    public CustomerItem? Current
    {
        get => _current;
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

    private void OpenCustomer(CustomerItem customer, string tab)
    {
        Current = customer;
        SelectTab(tab);
        FillDetail(customer);
        Go(AdminPage.Detail);
    }

    private void SelectTab(string tab)
    {
        _detailTab = tab;
        OnPropertyChanged(nameof(IsTabBase));
        OnPropertyChanged(nameof(IsTabPeople));
        OnPropertyChanged(nameof(IsTabRuns));
        OnPropertyChanged(nameof(IsTabFiles));
    }

    private void FillDetail(CustomerItem c)
    {
        Activities.Clear();
        Activities.Add(new ActivityItem { Text = "客户资料更新至 V2", Time = $"{c.Date} · {c.Owner} 确认" });
        Activities.Add(new ActivityItem { Text = "保存人员资料", Time = $"当前在册 {c.People} 人，可在人员记录中查阅" });
        Activities.Add(new ActivityItem { Text = "建立客户项目", Time = "10-12 09:20 · 归档初始交接资料" });

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
        OnPropertyChanged(nameof(HasRunRows));

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

    private void RefreshTasks()
    {
        var list = _allTasks.Where(t => _taskFilter == "全部" || t.IsPending);
        Tasks.Clear();
        foreach (var t in list) Tasks.Add(t);
        OnPropertyChanged(nameof(TaskSummary));
        OnPropertyChanged(nameof(PendingCount));
    }

    private void Decide(TaskItem task, string status)
    {
        task.Status = status;
        task.Note = status == "已退回"
            ? "已记录退回原因，并新增一条补材料待办。"
            : "审批已通过，结果已同步到工作台。";
        RefreshTasks();
    }

    // ===================== Workflow =====================
    public int WfStep
    {
        get => _wfStep;
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

    public string WfCustomerName => Current?.Name ?? "青禾新能源有限公司";
    public string WfCustomerCode => Current?.Code ?? "KH-001";

    private void StartRun()
    {
        if (!WfAccepted)
        {
            WfError = "请先确认使用虚构样例。";
            return;
        }

        WfError = string.Empty;
        WfRows.Clear();
        WfRows.Add(new ResultRow { Person = "演示员工 A", Business = 80, System = 100, Result = "金额差异", Status = "待复核" });
        WfRows.Add(new ResultRow { Person = "演示员工 B", Business = 45, System = 40, Result = "金额差异", Status = "待复核" });
        WfRows.Add(new ResultRow { Person = "演示员工 C", Business = 120, System = 120, Result = "金额一致", Status = "本项通过" });
        WfStep = 1;
    }

    private void ResolveRun()
    {
        if (string.IsNullOrWhiteSpace(WfNote))
        {
            WfError = "请填写复核说明。";
            return;
        }

        WfError = string.Empty;
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
        WfStep = 2;
    }

    private void ArchiveRun()
    {
        WfError = string.Empty;
        Go(AdminPage.Customers);
    }

    // ===================== KPI =====================
    public ObservableCollection<MetricRow> Metrics { get; } = new();

    private void BuildMetrics()
    {
        Metrics.Add(new MetricRow { Title = "任务处理", Value = "0 / 12", Note = "随演示操作更新" });
        Metrics.Add(new MetricRow { Title = "考勤异常（固定样例）", Value = "2 项", Note = "缺少打卡记录，需人工核实" });
        Metrics.Add(new MetricRow { Title = "平均响应（固定样例）", Value = "12 分钟", Note = "不用于真实考核" });
    }

    // ===================== 演示数据 =====================
    private void BuildCustomers()
    {
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
