using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Aster.ViewModels;

/// <summary>工作台对话消息。</summary>
public sealed class ChatMessage
{
    public string Author { get; init; } = "Agent";
    public string Text { get; init; } = string.Empty;
    public bool IsUser { get; init; }
    public bool IsAgent => !IsUser;
}

/// <summary>工作台顶部的快捷入口卡。</summary>
public sealed class WorkbenchCard
{
    public string Chip { get; init; } = string.Empty;
    public string Tone { get; init; } = "accent";
    public string Title { get; init; } = string.Empty;
    public string Desc { get; init; } = string.Empty;
    public string FlowKey { get; init; } = string.Empty;
    public ICommand Command { get; init; } = null!;

    public IBrush ChipBackground => Tone switch
    {
        "green" => Palette.Alpha(Palette.GreenColor, 0.12),
        "red" => Palette.Alpha(Palette.RedColor, 0.12),
        _ => Palette.Alpha(Palette.AccentColor, 0.14)
    };

    public IBrush ChipForeground => Tone switch
    {
        "green" => Palette.Solid(Palette.GreenColor),
        "red" => Palette.Solid(Palette.RedColor),
        _ => Palette.Solid("#9FB4FF")
    };
}

/// <summary>列表 / 抽屉里的通用条目。</summary>
public sealed class FlowItem
{
    public string Title { get; init; } = string.Empty;
    public string Meta { get; init; } = string.Empty;
    public string Tag { get; init; } = string.Empty;
    public string Tone { get; init; } = "muted";
    public string Key { get; init; } = string.Empty;
    public ICommand? Command { get; init; }

    public bool HasTag => !string.IsNullOrEmpty(Tag);
    public bool CanOpen => Command is not null && !string.IsNullOrEmpty(Key);

    public IBrush TagBrush => Tone switch
    {
        "green" => Palette.Solid(Palette.GreenColor),
        "amber" => Palette.Solid(Palette.AmberColor),
        "red" => Palette.Solid(Palette.RedColor),
        "accent" => Palette.Solid("#9FB4FF"),
        _ => Palette.Solid("#9BA3B7")
    };

    public IBrush TagBackground => Tone switch
    {
        "green" => Palette.Alpha(Palette.GreenColor, 0.10),
        "amber" => Palette.Alpha(Palette.AmberColor, 0.10),
        "red" => Palette.Alpha(Palette.RedColor, 0.10),
        "accent" => Palette.Alpha(Palette.AccentColor, 0.12),
        _ => Palette.Alpha("#FFFFFF", 0.06)
    };
}

/// <summary>一条流程（对应画布上的一块/一组画板）。</summary>
public sealed class FlowDefinition
{
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string DrawerTitle { get; init; } = "上下文";
    public string Badge { get; init; } = string.Empty;
    public string BadgeTone { get; init; } = "muted";
    public ChatMessage[] Messages { get; init; } = Array.Empty<ChatMessage>();
    public FlowItem[] Drawer { get; init; } = Array.Empty<FlowItem>();
    public FlowItem[] Footer { get; init; } = Array.Empty<FlowItem>();

    public bool HasFooter => Footer.Length > 0;
    public bool HasBadge => !string.IsNullOrEmpty(Badge);

    public IBrush BadgeBrush => BadgeTone switch
    {
        "green" => Palette.Solid(Palette.GreenColor),
        "amber" => Palette.Solid(Palette.AmberColor),
        "red" => Palette.Solid(Palette.RedColor),
        _ => Palette.Solid("#9BA3B7")
    };

    public IBrush BadgeBackground => BadgeTone switch
    {
        "green" => Palette.Alpha(Palette.GreenColor, 0.10),
        "amber" => Palette.Alpha(Palette.AmberColor, 0.10),
        "red" => Palette.Alpha(Palette.RedColor, 0.10),
        _ => Palette.Alpha("#FFFFFF", 0.06)
    };
}

/// <summary>管理模块：工作台 + 各区域流程页。</summary>
public sealed class AdminViewModel : ObservableObject
{
    private static readonly string[] Replies =
    {
        "好的，正在拉取数据，稍等我把结果贴到对话里。",
        "已记录。需要我同时生成结算单草稿吗？",
        "收到，我先按模板校验必填字段，异常项会列在下面。",
        "这个动作会影响 3 个关联项目，确认后我再执行。"
    };

    private static readonly Dictionary<string, FlowDefinition> Flows = new()
    {
        ["new-project"] = new FlowDefinition
        {
            Title = "新建项目",
            Subtitle = "清远电子 / 信息核查",
            DrawerTitle = "项目字段",
            Badge = "待确认",
            BadgeTone = "amber",
            Messages = new[]
            {
                new ChatMessage { Author = "张客服", IsUser = true, Text = "用「清远电子」建一个新项目，做 9 月的信息核查。" },
                new ChatMessage { Text = "可以。需要确认三项：客户主体、服务范围、结算口径。我先从合同里读出默认值，你确认后我创建项目工作空间。" },
                new ChatMessage { Author = "张客服", IsUser = true, Text = "服务范围加上首月结算，结算口径按合同走。" },
                new ChatMessage { Text = "已更新。确认后我会创建项目，并把「员工名单导入」Pipeline 挂到该项目下。" }
            },
            Drawer = new[]
            {
                new FlowItem { Title = "客户主体", Meta = "清远电子 · 合同 CT-2026-118", Tag = "已确认", Tone = "green" },
                new FlowItem { Title = "服务范围", Meta = "员工信息核查 + 首月结算", Tag = "待确认", Tone = "amber" },
                new FlowItem { Title = "结算口径", Meta = "服务费 128 元/人·月，社保按实缴", Tag = "待确认", Tone = "amber" },
                new FlowItem { Title = "启动时间", Meta = "2026-10-01 起", Tag = "已确认", Tone = "green" }
            }
        },

        ["kpi"] = new FlowDefinition
        {
            Title = "KPI 考勤",
            Subtitle = "清远电子 · 2026 年 9 月",
            DrawerTitle = "指标概览",
            Badge = "3 项异常",
            BadgeTone = "amber",
            Messages = new[]
            {
                new ChatMessage { Author = "张客服", IsUser = true, Text = "看一下 9 月清远电子的 KPI 和考勤异常。" },
                new ChatMessage { Text = "9 月出勤率 96.4%，异常 3 项：迟到 2 次、漏打卡 1 次。平均响应 2.4 小时，加班合计 128 小时。" },
                new ChatMessage { Author = "张客服", IsUser = true, Text = "把 3 条异常列出来，我要发给客户确认。" },
                new ChatMessage { Text = "已整理成表格放在右侧，涉及李娜（漏打卡）与王强、周敏（迟到）。发送前需要你确认措辞。" }
            },
            Drawer = new[]
            {
                new FlowItem { Title = "出勤率", Meta = "96.4% · 环比 +0.8%", Tag = "正常", Tone = "green" },
                new FlowItem { Title = "考勤异常", Meta = "迟到 2 · 漏打卡 1", Tag = "待处理", Tone = "amber" },
                new FlowItem { Title = "平均响应", Meta = "2.4 小时 · 目标 ≤ 4 小时", Tag = "达标", Tone = "green" },
                new FlowItem { Title = "加班合计", Meta = "128 小时 · 环比 +12%", Tag = "关注", Tone = "amber" }
            }
        },

        ["todos"] = new FlowDefinition
        {
            Title = "待办事项",
            Subtitle = "今天 · 12 条",
            DrawerTitle = "系统生成的待办",
            Badge = "12 条",
            BadgeTone = "red",
            Messages = new[]
            {
                new ChatMessage { Author = "张客服", IsUser = true, Text = "把今天需要我处理的待办拉成表格。" },
                new ChatMessage { Text = "已生成 12 条待办：3 条需要主管审批、2 条字段缺失、1 条客户账单今天到期。表格已挂在下方。" },
                new ChatMessage { Author = "张客服", IsUser = true, Text = "先看审批类的那 3 条。" },
                new ChatMessage { Text = "审批类都在右侧最上面的分组，点开任意一条可以进入审批节点详情。" }
            },
            Drawer = new[]
            {
                new FlowItem { Title = "审批 · 张伟 入职审批", Meta = "河南省中医院 · 12 分钟前", Tag = "待处理", Tone = "amber", Key = "approval" },
                new FlowItem { Title = "信息 · 员工名单字段缺失 3 条", Meta = "9 月名单 · 需补充社保编号", Tag = "待补充", Tone = "amber", Key = "project" },
                new FlowItem { Title = "进度 · 客户账单今天到期", Meta = "清远电子 · 需生成草稿", Tag = "今天", Tone = "red", Key = "settlement" },
                new FlowItem { Title = "口径 · 跨月补缴是否计入本月", Meta = "结算规则 · 待确认", Tag = "待确认", Tone = "accent", Key = "settlement" }
            }
        },

        ["settlement"] = new FlowDefinition
        {
            Title = "清远电子 / 首月结算",
            Subtitle = "任务进度 · 2026-10",
            DrawerTitle = "执行进度",
            Badge = "进行中",
            BadgeTone = "accent",
            Messages = new[]
            {
                new ChatMessage { Author = "张客服", IsUser = true, Text = "清远电子首月结算跑一遍，先给我看进度。" },
                new ChatMessage { Text = "名单导入和社保核对已完成；正在试算工资，预计 2 分钟。账单生成需要你确认口径后执行。" },
                new ChatMessage { Author = "张客服", IsUser = true, Text = "差异那一条是什么？" },
                new ChatMessage { Text = "李娜的社保基数与合同附件不一致（8,200 vs 8,500），已标记，需要你确认按哪个口径。" }
            },
            Drawer = new[]
            {
                new FlowItem { Title = "员工名单导入", Meta = "64 人 · 09:12 完成", Tag = "完成", Tone = "green" },
                new FlowItem { Title = "社保基数核对", Meta = "63 一致 · 1 差异", Tag = "完成", Tone = "green" },
                new FlowItem { Title = "工资试算", Meta = "进行中 · 预计 2 分钟", Tag = "进行中", Tone = "accent" },
                new FlowItem { Title = "账单生成", Meta = "等待口径确认", Tag = "待执行", Tone = "muted" }
            },
            Footer = new[]
            {
                new FlowItem { Title = "当前操作", Meta = "工资试算 · 64 人 · 可中断", Tag = "查看明细", Tone = "accent" }
            }
        },

        ["approval"] = new FlowDefinition
        {
            Title = "协作与审批 / 政策审核请求",
            Subtitle = "RQ-019 · 远航科技",
            DrawerTitle = "审批节点",
            Badge = "待我处理",
            BadgeTone = "amber",
            Messages = new[]
            {
                new ChatMessage { Author = "张客服", IsUser = true, Text = "远航科技的政策审核请求，证据齐了吗？" },
                new ChatMessage { Text = "RQ-019 已附 3 份证据：客户交接表、员工花名册、政策问题摘要。节点有效期至 10-24 18:00。" },
                new ChatMessage { Author = "张客服", IsUser = true, Text = "先通过，同时留一句备注让销售补充材料。" },
                new ChatMessage { Text = "已记录审批意见：通过，备注「请补充 9 月社保缴纳凭证」。结果会同步给发起人和财务。" }
            },
            Drawer = new[]
            {
                new FlowItem { Title = "发起人", Meta = "销售 李锐 · 10-20 14:12", Tag = "已提交", Tone = "green" },
                new FlowItem { Title = "审批人", Meta = "张客服 · 当前节点", Tag = "进行中", Tone = "accent" },
                new FlowItem { Title = "证据文件", Meta = "客户交接表.xlsx 等 3 份", Tag = "齐备", Tone = "green" },
                new FlowItem { Title = "期限", Meta = "2026-10-24 18:00", Tag = "剩余 2 天", Tone = "amber" }
            },
            Footer = new[]
            {
                new FlowItem { Title = "审批意见", Meta = "通过 · 备注待补充材料", Tag = "已记录", Tone = "green" }
            }
        },

        ["project"] = new FlowDefinition
        {
            Title = "河南省中医院 / 员工记录",
            Subtitle = "信息核查 · 9 月名单",
            DrawerTitle = "员工记录",
            Badge = "3 条缺失",
            BadgeTone = "amber",
            Messages = new[]
            {
                new ChatMessage { Author = "张客服", IsUser = true, Text = "打开河南省中医院的员工记录，核对本次导入。" },
                new ChatMessage { Text = "本次导入 128 人，字段完整 125 人，3 人缺少社保编号。写入前后差异与关联文件已附在右侧。" },
                new ChatMessage { Author = "张客服", IsUser = true, Text = "缺编号的先挂起，我让客户补。" },
                new ChatMessage { Text = "已把这 3 人标记为「待补充」，其余 125 人进入结算准备。" }
            },
            Drawer = new[]
            {
                new FlowItem { Title = "张伟", Meta = "护士 · 工号 0231 · 社保已验证", Tag = "完整", Tone = "green" },
                new FlowItem { Title = "李娜", Meta = "药师 · 工号 0187 · 缺社保编号", Tag = "待补充", Tone = "amber" },
                new FlowItem { Title = "王强", Meta = "后勤 · 工号 0302 · 社保已验证", Tag = "完整", Tone = "green" }
            },
            Footer = new[]
            {
                new FlowItem { Title = "关联文件", Meta = "员工花名册.xlsx · 客户交接表.xlsx", Tag = "2 份", Tone = "muted" }
            }
        }
    };

    private int _replyIndex;
    private string _activeKey = "workbench";
    private string _draft = string.Empty;
    private string _sectionTitle = "工作台";
    private string _sectionDesc = string.Empty;
    private string _sectionIcon = Controls.Icons.Grid;
    private bool _isWorkbench = true;

    public AdminViewModel()
    {
        SelectCommand = new RelayCommand(o =>
        {
            if (o is SubNavLinkEntry entry) Select(entry.Key);
        });
        CardCommand = new RelayCommand(o =>
        {
            if (o is WorkbenchCard card) OpenFlow(card.FlowKey);
        });
        OpenFlowCommand = new RelayCommand(o =>
        {
            if (o is FlowItem item && item.CanOpen) OpenFlow(item.Key);
        });
        BackCommand = new RelayCommand(() => Select("workbench"));
        SendCommand = new RelayCommand(Send);

        Cards = new ObservableCollection<WorkbenchCard>
        {
            new()
            {
                Chip = "创建", Tone = "accent", Title = "新建项目",
                Desc = "创建客户或项目工作空间。", FlowKey = "new-project", Command = CardCommand
            },
            new()
            {
                Chip = "3 项", Tone = "green", Title = "KPI 考勤",
                Desc = "查看绩效、考勤异常、响应时效。", FlowKey = "kpi", Command = CardCommand
            },
            new()
            {
                Chip = "12 条", Tone = "red", Title = "待办事项",
                Desc = "审批、导入、账单到期与协作请求。", FlowKey = "todos", Command = CardCommand
            }
        };

        Messages = new ObservableCollection<ChatMessage>
        {
            new() { Author = "Agent", Text = "早上好。今天有 12 条待办，其中 3 条需要主管审批，2 个客户账单今天到期。" },
            new() { Author = "张客服", IsUser = true, Text = "先帮我创建河南省中医院项目，并把 9 月员工名单导入挂到这个项目下。" },
            new() { Author = "Agent", Text = "可以。请上传 9 月员工名单，我会先核对必填字段，再请你确认导入。" }
        };

        FlowMessages = new ObservableCollection<ChatMessage>();
        Rows = new ObservableCollection<FlowItem>();
        Drawer = new ObservableCollection<FlowItem>();
        DrawerFooter = new ObservableCollection<FlowItem>();
    }

    public ObservableCollection<WorkbenchCard> Cards { get; }
    public ObservableCollection<ChatMessage> Messages { get; }
    public ObservableCollection<ChatMessage> FlowMessages { get; }
    public ObservableCollection<FlowItem> Rows { get; }
    public ObservableCollection<FlowItem> Drawer { get; }
    public ObservableCollection<FlowItem> DrawerFooter { get; }

    public ICommand SelectCommand { get; }
    public ICommand CardCommand { get; }
    public ICommand OpenFlowCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand SendCommand { get; }

    public string ActiveKey
    {
        get => _activeKey;
        private set => Set(ref _activeKey, value);
    }

    public bool IsWorkbench
    {
        get => _isWorkbench;
        private set
        {
            if (!Set(ref _isWorkbench, value)) return;
            OnPropertyChanged(nameof(IsSection));
            OnPropertyChanged(nameof(IsFlow));
            OnPropertyChanged(nameof(TopBarTitle));
            OnPropertyChanged(nameof(ActiveMessages));
        }
    }

    public bool IsSection { get; private set; }
    public bool IsFlow { get; private set; }

    public string TopBarTitle => IsFlow ? FlowTitle : (_isWorkbench ? "工作台" : "工作区");

    public ObservableCollection<ChatMessage> ActiveMessages => _isWorkbench ? Messages : FlowMessages;

    public string SectionTitle
    {
        get => _sectionTitle;
        private set => Set(ref _sectionTitle, value);
    }

    public string SectionDesc
    {
        get => _sectionDesc;
        private set => Set(ref _sectionDesc, value);
    }

    public string SectionIcon
    {
        get => _sectionIcon;
        private set => Set(ref _sectionIcon, value);
    }

    // ---------- 流程页 ----------
    private string _flowTitle = string.Empty;
    private string _flowSubtitle = string.Empty;
    private string _drawerTitle = "上下文";
    private string _flowBadge = string.Empty;
    private IBrush _flowBadgeBrush = Palette.Solid("#9BA3B7");
    private IBrush _flowBadgeBg = Palette.Alpha("#FFFFFF", 0.06);
    private bool _hasFlowBadge;
    private bool _hasDrawerFooter;

    public string FlowTitle
    {
        get => _flowTitle;
        private set
        {
            if (!Set(ref _flowTitle, value)) return;
            OnPropertyChanged(nameof(TopBarTitle));
        }
    }

    public string FlowSubtitle
    {
        get => _flowSubtitle;
        private set => Set(ref _flowSubtitle, value);
    }

    public string DrawerTitle
    {
        get => _drawerTitle;
        private set => Set(ref _drawerTitle, value);
    }

    public string FlowBadge
    {
        get => _flowBadge;
        private set => Set(ref _flowBadge, value);
    }

    public IBrush FlowBadgeBrush
    {
        get => _flowBadgeBrush;
        private set => Set(ref _flowBadgeBrush, value);
    }

    public IBrush FlowBadgeBackground
    {
        get => _flowBadgeBg;
        private set => Set(ref _flowBadgeBg, value);
    }

    public bool HasFlowBadge
    {
        get => _hasFlowBadge;
        private set => Set(ref _hasFlowBadge, value);
    }

    public bool HasDrawerFooter
    {
        get => _hasDrawerFooter;
        private set => Set(ref _hasDrawerFooter, value);
    }

    public string Draft
    {
        get => _draft;
        set => Set(ref _draft, value);
    }

    public string UserName => "张客服";
    public string UserInitial => "张";

    public void Select(string key)
    {
        ActiveKey = key;

        if (key == "workbench")
        {
            SectionTitle = "工作台";
            SetState(workbench: true);
            return;
        }

        // 最近记录复用对应流程
        var alias = key switch
        {
            "recent-1" => "settlement",
            "recent-2" => "project",
            "recent-3" => "approval",
            _ => key
        };

        if (Flows.TryGetValue(alias, out var flow))
        {
            LoadFlow(flow);
            return;
        }

        var (title, desc, icon, rows) = Describe(key);
        SectionTitle = title;
        SectionDesc = desc;
        SectionIcon = icon;
        Rows.Clear();
        foreach (var row in rows)
            Rows.Add(new FlowItem
            {
                Title = row.Title, Meta = row.Meta, Tag = row.Tag, Tone = row.Tone,
                Key = row.Key, Command = OpenFlowCommand
            });
        SetState(workbench: false);
    }

    private void OpenFlow(string key)
    {
        if (Flows.TryGetValue(key, out var flow)) LoadFlow(flow);
        else Select(key);
    }

    private void LoadFlow(FlowDefinition flow)
    {
        FlowTitle = flow.Title;
        FlowSubtitle = flow.Subtitle;
        DrawerTitle = flow.DrawerTitle;
        FlowBadge = flow.Badge;
        HasFlowBadge = flow.HasBadge;
        FlowBadgeBrush = flow.BadgeBrush;
        FlowBadgeBackground = flow.BadgeBackground;

        FlowMessages.Clear();
        foreach (var m in flow.Messages) FlowMessages.Add(m);

        Drawer.Clear();
        foreach (var d in flow.Drawer) Drawer.Add(new FlowItem
        {
            Title = d.Title, Meta = d.Meta, Tag = d.Tag, Tone = d.Tone, Key = d.Key,
            Command = OpenFlowCommand
        });

        DrawerFooter.Clear();
        foreach (var f in flow.Footer) DrawerFooter.Add(f);
        HasDrawerFooter = flow.HasFooter;

        SetState(workbench: false, flow: true);
    }

    private void SetState(bool workbench, bool flow = false)
    {
        IsWorkbench = workbench;
        IsFlow = flow;
        IsSection = !workbench && !flow;
        OnPropertyChanged(nameof(IsSection));
        OnPropertyChanged(nameof(IsFlow));
        OnPropertyChanged(nameof(TopBarTitle));
        OnPropertyChanged(nameof(ActiveMessages));
    }

    private void Send()
    {
        var text = Draft?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        var list = ActiveMessages;
        list.Add(new ChatMessage { Author = "张客服", Text = text, IsUser = true });
        Draft = string.Empty;

        var reply = Replies[_replyIndex++ % Replies.Length];
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            list.Add(new ChatMessage { Author = "Agent", Text = reply });
        };
        timer.Start();
    }

    private static (string Title, string Desc, string Icon, FlowItem[] Rows) Describe(string key) => key switch
    {
        "clients" => ("客户工作空间", "按客户组织项目、合同与员工档案。", Controls.Icons.Users, new[]
        {
            new FlowItem { Title = "河南省中医院", Meta = "在办项目 2 个 · 员工 128 人", Tag = "进行中", Tone = "green", Key = "project" },
            new FlowItem { Title = "清远电子", Meta = "首月结算 · 员工 64 人", Tag = "待结算", Tone = "amber", Key = "settlement" },
            new FlowItem { Title = "恒川制造", Meta = "员工增减 · 本月新增 12 人", Tag = "进行中", Tone = "green", Key = "project" },
            new FlowItem { Title = "远航科技", Meta = "政策审核 · 等待材料补充", Tag = "待处理", Tone = "accent", Key = "approval" }
        }),

        "import" => ("信息导入中心", "批量导入员工名单、考勤与账单数据。", Controls.Icons.Download, new[]
        {
            new FlowItem { Title = "9 月员工名单.xlsx", Meta = "张客服 · 今天 09:12 · 128 行", Tag = "已导入", Tone = "green", Key = "project" },
            new FlowItem { Title = "8 月考勤汇总.xlsx", Meta = "张客服 · 昨天 17:40 · 126 行", Tag = "已导入", Tone = "green", Key = "kpi" },
            new FlowItem { Title = "清远电子 账单.csv", Meta = "系统 · 昨天 14:02 · 待校验", Tag = "待校验", Tone = "amber", Key = "settlement" }
        }),

        "account" => ("账号与权限", "按部门与客户范围分配访问权限。", Controls.Icons.Users, new[]
        {
            new FlowItem { Title = "张客服 · 客服", Meta = "部门：客户成功 · 全部客户", Tag = "全部权限", Tone = "accent" },
            new FlowItem { Title = "李锐 · 销售", Meta = "部门：销售 · 客户：远航科技", Tag = "受限", Tone = "amber" },
            new FlowItem { Title = "王敏 · 财务", Meta = "部门：财务 · 基础结算", Tag = "受限", Tone = "amber" }
        }),

        _ => ("工作区", "从常用入口开始，也可以直接在下方对话中交办工作。", Controls.Icons.Grid, Array.Empty<FlowItem>())
    };
}
