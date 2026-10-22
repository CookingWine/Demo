using System;
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
    public string Prompt { get; init; } = string.Empty;
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

/// <summary>通用分区页的一行。</summary>
public sealed class SectionRow
{
    public string Title { get; init; } = string.Empty;
    public string Meta { get; init; } = string.Empty;
    public string Tag { get; init; } = string.Empty;
    public string Tone { get; init; } = "muted";

    public bool HasTag => !string.IsNullOrEmpty(Tag);

    public IBrush TagBrush => Tone switch
    {
        "green" => Palette.Solid(Palette.GreenColor),
        "amber" => Palette.Solid(Palette.AmberColor),
        "accent" => Palette.Solid("#9FB4FF"),
        _ => Palette.Solid("#9BA3B7")
    };

    public IBrush TagBackground => Tone switch
    {
        "green" => Palette.Alpha(Palette.GreenColor, 0.10),
        "amber" => Palette.Alpha(Palette.AmberColor, 0.10),
        "accent" => Palette.Alpha(Palette.AccentColor, 0.12),
        _ => Palette.Alpha("#FFFFFF", 0.06)
    };
}

/// <summary>管理模块：今日工作台（快捷入口 + 对话 + 分区页）。</summary>
public sealed class AdminViewModel : ObservableObject
{
    private static readonly string[] Replies =
    {
        "好的，正在拉取数据，稍等我把结果贴到对话里。",
        "已记录。需要我同时生成结算单草稿吗？",
        "收到，我先按模板校验必填字段，异常项会列在下面。",
        "这个动作会影响 3 个关联项目，确认后我再执行。"
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
            if (o is WorkbenchCard card && !string.IsNullOrEmpty(card.Prompt)) Draft = card.Prompt;
        });
        SendCommand = new RelayCommand(Send);

        Cards = new ObservableCollection<WorkbenchCard>
        {
            new()
            {
                Chip = "创建", Tone = "accent", Title = "新建项目",
                Desc = "创建客户或项目工作空间。",
                Prompt = "帮我新建一个项目工作空间",
                Command = CardCommand
            },
            new()
            {
                Chip = "3 项", Tone = "green", Title = "KPI 考勤",
                Desc = "查看绩效、考勤异常、响应时效。",
                Prompt = "看一下本周的 KPI 与考勤异常",
                Command = CardCommand
            },
            new()
            {
                Chip = "12 条", Tone = "red", Title = "待办事项",
                Desc = "审批、导入、账单到期与协作请求。",
                Prompt = "列出我今天需要处理的 12 条待办",
                Command = CardCommand
            }
        };

        Messages = new ObservableCollection<ChatMessage>
        {
            new()
            {
                Author = "Agent",
                Text = "早上好。今天有 12 条待办，其中 3 条需要主管审批，2 个客户账单今天到期。"
            },
            new()
            {
                Author = "张客服", IsUser = true,
                Text = "先帮我创建河南省中医院项目，并把 9 月员工名单导入挂到这个项目下。"
            },
            new()
            {
                Author = "Agent",
                Text = "可以。请上传 9 月员工名单，我会先核对必填字段，再请你确认导入。"
            }
        };

        Rows = new ObservableCollection<SectionRow>();
    }

    public ObservableCollection<WorkbenchCard> Cards { get; }
    public ObservableCollection<ChatMessage> Messages { get; }
    public ObservableCollection<SectionRow> Rows { get; }

    public ICommand SelectCommand { get; }
    public ICommand CardCommand { get; }
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
            OnPropertyChanged(nameof(TopBarTitle));
        }
    }

    public bool IsSection => !_isWorkbench;

    /// <summary>顶部条标题：工作台页显示「工作台」，分区页显示模块名。</summary>
    public string TopBarTitle => _isWorkbench ? "工作台" : "工作区";

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

    public string Draft
    {
        get => _draft;
        set
        {
            if (!Set(ref _draft, value)) return;
            (SendCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string UserName => "张客服";
    public string UserInitial => "张";

    public void Select(string key)
    {
        ActiveKey = key;
        if (key == "workbench")
        {
            SectionTitle = "工作台";
            IsWorkbench = true;
            return;
        }

        var (title, desc, icon, rows) = Describe(key);
        SectionTitle = title;
        SectionDesc = desc;
        SectionIcon = icon;
        Rows.Clear();
        foreach (var row in rows) Rows.Add(row);
        IsWorkbench = false;
    }

    private static (string Title, string Desc, string Icon, SectionRow[] Rows) Describe(string key) => key switch
    {
        "clients" => ("客户工作空间", "按客户组织项目、合同与员工档案。", Controls.Icons.Users, new[]
        {
            new SectionRow { Title = "河南省中医院", Meta = "在办项目 2 个 · 员工 128 人", Tag = "进行中", Tone = "green" },
            new SectionRow { Title = "清远电子", Meta = "首月结算 · 员工 64 人", Tag = "待结算", Tone = "amber" },
            new SectionRow { Title = "恒川制造", Meta = "员工增减 · 本月新增 12 人", Tag = "进行中", Tone = "green" },
            new SectionRow { Title = "远航科技", Meta = "政策审核 · 等待材料补充", Tag = "待处理", Tone = "accent" }
        }),

        "import" => ("信息导入中心", "批量导入员工名单、考勤与账单数据。", Controls.Icons.Download, new[]
        {
            new SectionRow { Title = "9 月员工名单.xlsx", Meta = "张客服 · 今天 09:12 · 128 行", Tag = "已导入", Tone = "green" },
            new SectionRow { Title = "8 月考勤汇总.xlsx", Meta = "张客服 · 昨天 17:40 · 126 行", Tag = "已导入", Tone = "green" },
            new SectionRow { Title = "清远电子 账单.csv", Meta = "系统 · 昨天 14:02 · 待校验", Tag = "待校验", Tone = "amber" }
        }),

        "settlement" => ("基础结算", "按服务费与社保口径生成结算单。", Controls.Icons.Currency, new[]
        {
            new SectionRow { Title = "清远电子 · 首月服务费", Meta = "计费周期 2026-10 · 64 人", Tag = "¥86,400", Tone = "accent" },
            new SectionRow { Title = "河南省中医院 · 9 月服务费", Meta = "计费周期 2026-09 · 128 人", Tag = "¥172,800", Tone = "accent" },
            new SectionRow { Title = "恒川制造 · 增减调整", Meta = "本月新增 12 人 · 待确认", Tag = "待确认", Tone = "amber" }
        }),

        "approval" => ("协作与审批", "集中处理审批、协作请求与超时提醒。", Controls.Icons.List, new[]
        {
            new SectionRow { Title = "员工入职审批 · 河南省中医院", Meta = "张客服 提交 · 12 分钟前", Tag = "待我处理", Tone = "amber" },
            new SectionRow { Title = "9 月结算单确认", Meta = "财务 王敏 · 1 小时前", Tag = "待我处理", Tone = "amber" },
            new SectionRow { Title = "合同续签 · 清远电子", Meta = "销售 李锐 · 昨天", Tag = "已通过", Tone = "green" }
        }),

        "project" => ("河南省中医院", "项目工作空间：合同、员工、结算与协作记录。", Controls.Icons.Folder, new[]
        {
            new SectionRow { Title = "9 月员工名单", Meta = "128 人 · 已导入", Tag = "已导入", Tone = "green" },
            new SectionRow { Title = "服务合同 2026", Meta = "有效期至 2027-03 · 扫描件", Tag = "已归档", Tone = "muted" },
            new SectionRow { Title = "9 月服务费结算", Meta = "¥172,800 · 待确认", Tag = "待确认", Tone = "amber" }
        }),

        "recent-1" => ("清远电子 · 首月结算", "从最近的对话继续处理。", Controls.Icons.Clock, new[]
        {
            new SectionRow { Title = "核对 64 人社保基数", Meta = "昨天 18:20", Tag = "已完成", Tone = "green" },
            new SectionRow { Title = "生成首月结算单", Meta = "昨天 17:05", Tag = "待确认", Tone = "amber" }
        }),

        "recent-2" => ("恒川制造 · 员工增减", "从最近的对话继续处理。", Controls.Icons.Clock, new[]
        {
            new SectionRow { Title = "本月新增 12 人名单", Meta = "今天 09:40", Tag = "待导入", Tone = "amber" },
            new SectionRow { Title = "离职 3 人手续", Meta = "昨天 15:12", Tag = "已完成", Tone = "green" }
        }),

        "recent-3" => ("远航科技 · 政策审核", "从最近的对话继续处理。", Controls.Icons.Clock, new[]
        {
            new SectionRow { Title = "补充材料清单", Meta = "昨天 11:30", Tag = "待补充", Tone = "amber" },
            new SectionRow { Title = "审核意见回执", Meta = "10-20", Tag = "已回复", Tone = "green" }
        }),

        _ => ("工作台", "从常用入口开始，也可以直接在下方对话中交办工作。", Controls.Icons.Grid, Array.Empty<SectionRow>())
    };

    private void Send()
    {
        var text = Draft?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        Messages.Add(new ChatMessage { Author = "张客服", Text = text, IsUser = true });
        Draft = string.Empty;

        var reply = Replies[_replyIndex++ % Replies.Length];
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Messages.Add(new ChatMessage { Author = "Agent", Text = reply });
        };
        timer.Start();
    }
}
