// =============================================================================
// AdminViewModel.cs —— 「管理」模块唯一的 ViewModel
// 职责：为一个页面提供三种互斥状态的完整数据与命令：工作台 / 列表页 / 流程页。
// 三态：由 SetState(workbench, flow) 统一维护，IsWorkbench、IsFlow、IsSection 三者互斥，
//       AdminView.axaml 里三个并列布局各自 IsVisible 绑定其中一个布尔值来切换。
// 数据：Flows 是编译期写死的静态字典（6 套演示流程），用于替代真实服务端接口；
//       二级导航里没有独立流程的 key（clients / import / account 等）由 Describe 现造行数据。
// 易踩：IsSection / IsFlow 是自动属性，赋值不触发通知；TopBarTitle、ActiveMessages 是派生属性，
//       同样不会被自动通知。所有状态跳转都必须走 SetState，由它手动补发这四条通知。
// =============================================================================
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Aster.ViewModels;

/// <summary>工作台对话消息。</summary>
/// <remarks>
/// 同一条消息类型同时服务于工作台对话区和流程页对话区，渲染时靠 IsUser / IsAgent 二选一。
/// 所有属性都是 init-only，构造后即不可变：Send 里每次直接 new 一条追加进集合即可，
/// 因此这里刻意不实现 INotifyPropertyChanged——集合本身的变化就足以驱动界面刷新。
/// </remarks>
public sealed class ChatMessage
{
    // 显示名。Agent 消息不赋值时用默认值 "Agent"；用户消息在构造处显式写 "张客服"。
    // 它只作展示用，真正的左右布局判定看 IsUser，不要靠 Author 判断。
    public string Author { get; init; } = "Agent";
    // 消息正文，纯文本；换行与自动折行都交给模板里的 TextWrapping 处理。
    public string Text { get; init; } = string.Empty;
    // 是否由当前用户发出。true 走右侧浅蓝气泡模板，false 走左侧无边框模板。
    public bool IsUser { get; init; }
    // 派生属性，与 IsUser 严格互斥；XAML 里两个模板的 IsVisible 直接绑它，省一层取反转换器。
    public bool IsAgent => !IsUser;
}

/// <summary>工作台顶部的快捷入口卡。</summary>
/// <remarks>
/// 三张卡在 AdminViewModel 构造函数里硬编码生成，数量与 UniformGrid Columns=3 的栅格一致。
/// 整卡可点：XAML 用透明 Button 包住 Border.wcard，Command / CommandParameter 绑定在 Button 上，
/// 所以卡片本身不需要额外的鼠标事件处理。
/// </remarks>
public sealed class WorkbenchCard
{
    // 左上角小胶囊文字，用来承载「动作类型」或「数量」，例如「创建」「3 项」「12 条」。
    public string Chip { get; init; } = string.Empty;
    // 胶囊配色分支，取值："accent"（默认，蓝紫）、"green"（正常/达标）、"red"（告警/积压）。
    // 它本身不是颜色，而是喂给下面两个计算属性做 switch 的语义标签。
    public string Tone { get; init; } = "accent";
    // 卡片主标题，字号 21 的那一行。
    public string Title { get; init; } = string.Empty;
    // 卡片副标题，一到两行说明这个入口能做什么。
    public string Desc { get; init; } = string.Empty;
    // 点击后要打开的流程 key，必须能在 AdminViewModel.Flows 里命中，否则会退化成空的工作区。
    public string FlowKey { get; init; } = string.Empty;
    // 点击命令。声明成不可空（null!）是因为 Cards 里每张卡都会赋 CardCommand；
    // 若将来改成数据驱动而漏赋值，XAML 绑定会静默失效而不是抛异常，排查时先看这里。
    public ICommand Command { get; init; } = null!;

    // 胶囊底色：同色系低透明度，保证不抢主标题的视觉重量。
    public IBrush ChipBackground => Tone switch
    {
        "green" => Palette.Alpha(Palette.GreenColor, 0.12),
        "red" => Palette.Alpha(Palette.RedColor, 0.12),
        _ => Palette.Alpha(Palette.AccentColor, 0.14)
    };

    // 胶囊文字色。accent 分支刻意用 #9FB4FF 而不是 AccentColor，让浅色底上的字更亮一点。
    public IBrush ChipForeground => Tone switch
    {
        "green" => Palette.Solid(Palette.GreenColor),
        "red" => Palette.Solid(Palette.RedColor),
        _ => Palette.Solid("#9FB4FF")
    };
}

/// <summary>列表 / 抽屉里的通用条目。</summary>
/// <remarks>
/// 一个类型服务三处：列表页 Rows（外层套 flatrow 样式，用横线分隔）、流程页右侧抽屉 Drawer
/// （套 dcard 样式，圆角卡片）、以及抽屉底部分隔线以下的 DrawerFooter。
/// 三处数据形状完全一致，只靠 XAML 里的边框样式区分，所以不必拆成多个类型。
/// </remarks>
public sealed class FlowItem
{
    // 主文案：客户名、任务名、员工姓名等。
    public string Title { get; init; } = string.Empty;
    // 次要说明，跟在标题下面一行，常见格式是「主体 · 数量 / 时间 / 状态」。
    public string Meta { get; init; } = string.Empty;
    // 右侧状态胶囊文字；空字符串表示不渲染胶囊，与 HasTag 配对使用。
    public string Tag { get; init; } = string.Empty;
    // 状态色调，取值："muted"（默认灰）、"green"（完成/正常）、"amber"（待处理/关注）、
    // "red"（告警/逾期）、"accent"（进行中/待确认）。只作为 switch 的语义标签，不是颜色值。
    public string Tone { get; init; } = "muted";
    // 点击后要跳转的目标流程 key。可空语义：账号与权限这类纯展示行不填，也就没有跳转。
    public string Key { get; init; } = string.Empty;
    // 点击命令，可空。Flows 里的原始数据刻意不挂命令（静态字典无法引用实例命令），
    // 由 Select / LoadFlow 在克隆时统一补上 OpenFlowCommand。
    public ICommand? Command { get; init; }

    // 是否渲染状态胶囊。用「文字是否为空」判断，省掉在 XAML 里写字符串比较。
    public bool HasTag => !string.IsNullOrEmpty(Tag);
    // 是否可点击打开：命令与目标 key 两者齐备才成立，XAML 用它控制右侧箭头显隐。
    // 注意它只影响「看起来可点」，绑定失效时 Button 仍在，只是点了没反应。
    public bool CanOpen => Command is not null && !string.IsNullOrEmpty(Key);

    // 胶囊文字色，按 Tone 映射到调色板里的实色。
    public IBrush TagBrush => Tone switch
    {
        "green" => Palette.Solid(Palette.GreenColor),
        "amber" => Palette.Solid(Palette.AmberColor),
        "red" => Palette.Solid(Palette.RedColor),
        "accent" => Palette.Solid("#9FB4FF"),
        _ => Palette.Solid("#9BA3B7")
    };

    // 胶囊底色，统一取 10%~12% 透明度；muted 分支用白色低透明度，保证深色背景上仍可见。
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
/// <remarks>
/// 一条流程 = 左栏对话脚本 + 右栏抽屉条目 + 可选的抽屉底部条目。
/// 三块数据打包在同一个对象里，LoadFlow 一次就能把整个流程页填满，不必再回查其它来源。
/// </remarks>
public sealed class FlowDefinition
{
    // 流程页主标题，同时经 TopBarTitle 反映到顶部任务条。
    public string Title { get; init; } = string.Empty;
    // 标题下方的说明行，约定格式为「主体 · 期间」。
    public string Subtitle { get; init; } = string.Empty;
    // 右侧抽屉的标题，未指定时用默认值「上下文」。
    public string DrawerTitle { get; init; } = "上下文";
    // 标题右侧的状态胶囊文字，如「待确认」「3 项异常」；空值表示这一页没有状态胶囊。
    public string Badge { get; init; } = string.Empty;
    // 状态胶囊色调，取值："muted"（默认灰）、"green"、"amber"、"red"、"accent"。
    public string BadgeTone { get; init; } = "muted";
    // 左栏对话脚本，按数组顺序铺开；用户与 Agent 消息靠 ChatMessage.IsUser 区分左右。
    public ChatMessage[] Messages { get; init; } = Array.Empty<ChatMessage>();
    // 右栏抽屉条目，自上而下的分组列表。
    public FlowItem[] Drawer { get; init; } = Array.Empty<FlowItem>();
    // 抽屉底部分隔线以下的补充条目，多数流程为空（例如「当前操作」「审批意见」）。
    public FlowItem[] Footer { get; init; } = Array.Empty<FlowItem>();

    // 是否有底部条目。XAML 里分隔线和底部 ItemsControl 都绑它，保证两者同进同出。
    public bool HasFooter => Footer.Length > 0;
    // 是否显示标题旁的状态胶囊，空字符串视为没有。
    public bool HasBadge => !string.IsNullOrEmpty(Badge);

    // 胶囊文字色，按 BadgeTone 映射到调色板实色。
    public IBrush BadgeBrush => BadgeTone switch
    {
        "green" => Palette.Solid(Palette.GreenColor),
        "amber" => Palette.Solid(Palette.AmberColor),
        "red" => Palette.Solid(Palette.RedColor),
        _ => Palette.Solid("#9BA3B7")
    };

    // 胶囊底色，与 BadgeBrush 成对出现，透明度比文字色低一档。
    public IBrush BadgeBackground => BadgeTone switch
    {
        "green" => Palette.Alpha(Palette.GreenColor, 0.10),
        "amber" => Palette.Alpha(Palette.AmberColor, 0.10),
        "red" => Palette.Alpha(Palette.RedColor, 0.10),
        _ => Palette.Alpha("#FFFFFF", 0.06)
    };
}

/// <summary>管理模块：工作台 + 各区域流程页。</summary>
/// <remarks>
/// 一个 ViewModel 承担三种互斥状态：工作台（IsWorkbench）、列表页（IsSection）、流程页（IsFlow）。
/// 状态切换只允许走 SetState，因为它额外负责补发派生属性的变更通知。
/// 所有集合（Cards / Messages / Rows / Drawer …）都是只读属性的 ObservableCollection，
/// 生命周期内实例不变，只做 Clear + Add，这样 XAML 绑定一次即可持续生效。
/// </remarks>
public sealed class AdminViewModel : ObservableObject
{
    // Agent 回复池。Send 按下标轮转取用，让连续发送时回复不重复；
    // 换成真实服务端时只需把「取 reply」换成异步请求，UI 层无需改动。
    private static readonly string[] Replies =
    {
        "好的，正在拉取数据，稍等我把结果贴到对话里。",
        "已记录。需要我同时生成结算单草稿吗？",
        "收到，我先按模板校验必填字段，异常项会列在下面。",
        "这个动作会影响 3 个关联项目，确认后我再执行。"
    };

    // -------------------------------------------------------------------------
    // 演示数据总表：key 同时是二级导航的 key、快捷卡的 FlowKey、以及 FlowItem.Key。
    // 为什么做成静态只读字典，而不是从服务端拉：
    // 1) 本项目是零后端的外观演示工程，静态数据保证每次启动的界面完全一致，便于对照设计稿；
    // 2) static 只初始化一次，多个 ViewModel 实例共享同一份，切页面无需重新加载；
    // 3) readonly 保证运行期不被改写，需要可变的部分（命令）在 Select / LoadFlow 里克隆时再补。
    // -------------------------------------------------------------------------
    private static readonly Dictionary<string, FlowDefinition> Flows = new()
    {
        // 场景：从零创建客户/项目工作空间，重点是「先确认字段、再创建」，所以抽屉全是待确认字段。
        ["new-project"] = new FlowDefinition
        {
            Title = "新建项目",
            Subtitle = "清远电子 / 信息核查",
            DrawerTitle = "项目字段",
            Badge = "待确认",
            BadgeTone = "amber",
            // 对话四拍：提需求 → Agent 反问三项 → 用户补充 → Agent 承诺创建并挂 Pipeline。
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

        // 场景：客户维度的绩效与考勤看板，重点是「指标 + 异常明细」，胶囊里带环比。
        ["kpi"] = new FlowDefinition
        {
            Title = "KPI 考勤",
            Subtitle = "清远电子 · 2026 年 9 月",
            DrawerTitle = "指标概览",
            Badge = "3 项异常",
            BadgeTone = "amber",
            // 对话四拍：问 KPI → 播报数字 → 要异常明细 → 给出具体人名。
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

        // 场景：当天待办聚合。这一组的 Drawer 条目带 Key，点击可二次跳转到 approval / project / settlement，
        // 是全表里唯一「抽屉内还能继续下钻」的流程。
        ["todos"] = new FlowDefinition
        {
            Title = "待办事项",
            Subtitle = "今天 · 12 条",
            DrawerTitle = "系统生成的待办",
            Badge = "12 条",
            BadgeTone = "red",
            // 对话四拍：要表 → 汇总 12 条分类 → 只看审批类 → 指引到右侧分组。
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

        // 场景：首月结算执行过程，重点是四步流水线的进度（导入 → 核对 → 试算 → 账单）；
        // 带 Footer，用抽屉底部的「当前操作」说明正在跑的那一步。
        ["settlement"] = new FlowDefinition
        {
            Title = "清远电子 / 首月结算",
            Subtitle = "任务进度 · 2026-10",
            DrawerTitle = "执行进度",
            Badge = "进行中",
            BadgeTone = "accent",
            // 对话四拍：看进度 → 三步已完成、试算中 → 追问差异 → 说明社保基数不一致。
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

        // 场景：跨岗位协作的审批请求，抽屉按「发起人 → 审批人 → 证据 → 期限」排节点，
        // Footer 记录已录入的审批意见。
        ["approval"] = new FlowDefinition
        {
            Title = "协作与审批 / 政策审核请求",
            Subtitle = "RQ-019 · 远航科技",
            DrawerTitle = "审批节点",
            Badge = "待我处理",
            BadgeTone = "amber",
            // 对话四拍：问证据 → 列出 3 份材料与期限 → 要求通过并备注 → 记录审批意见。
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

        // 场景：项目内的员工档案核查，抽屉是员工列表（带缺失字段标记），
        // Footer 放「关联文件」作为佐证材料入口。
        ["project"] = new FlowDefinition
        {
            Title = "河南省中医院 / 员工记录",
            Subtitle = "信息核查 · 9 月名单",
            DrawerTitle = "员工记录",
            Badge = "3 条缺失",
            BadgeTone = "amber",
            // 对话四拍：打开名单 → 报 125/128 字段完整 → 缺失先挂起 → 其余进入结算准备。
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

    // 回复轮转下标。每次发送自增，对 Replies.Length 取模，保证循环取用不越界。
    private int _replyIndex;
    // 当前激活的导航 key。左侧二级导航用它和自身 Key 比较来决定高亮项，
    // 首次进入时是工作台，所以这里预置 "workbench"。
    private string _activeKey = "workbench";
    // 输入框双向绑定的草稿文本。工作台的 Composer 与流程页的输入框共用这一个属性，
    // 因为同一时刻只会显示其中一个，切换页面时不会互相干扰。
    private string _draft = string.Empty;
    // 列表页的三要素：标题、描述、图标。从 Describe 的元组里拆出来分别绑定。
    private string _sectionTitle = "工作台";
    private string _sectionDesc = string.Empty;
    private string _sectionIcon = Controls.Icons.Grid;
    // 三态中的「工作台」标记。另两个（IsSection / IsFlow）是自动属性，见下方说明。
    private bool _isWorkbench = true;

    /// <summary>
    /// 构造时一次性装配命令与初始数据。ViewModel 没有依赖注入参数，所有内容都在这里自给自足。
    /// </summary>
    public AdminViewModel()
    {
        // 导航命令：参数是二级导航条目本身，取出 Key 交给 Select。
        // 这里用「参数即数据上下文」的写法，避免为每个导航项单独开一个命令。
        SelectCommand = new RelayCommand(o =>
        {
            if (o is SubNavLinkEntry entry) Select(entry.Key);
        });
        // 快捷卡命令：三张卡共用同一个命令，靠 CommandParameter 传来的是哪张卡来分流。
        CardCommand = new RelayCommand(o =>
        {
            if (o is WorkbenchCard card) OpenFlow(card.FlowKey);
        });
        // 抽屉 / 列表行命令：同样一套命令复用，先校验 CanOpen 再跳转，
        // 这样不可跳转的行点击后什么也不会发生。
        OpenFlowCommand = new RelayCommand(o =>
        {
            if (o is FlowItem item && item.CanOpen) OpenFlow(item.Key);
        });
        // 返回工作台：等价于选中工作台这一项，走同一条状态收敛路径。
        BackCommand = new RelayCommand(() => Select("workbench"));
        // 发送消息：真正的逻辑在 Send 里，这里只做命令包装。
        SendCommand = new RelayCommand(Send);

        // 工作台三张快捷卡，顺序即 UniformGrid 里的 1/2/3 列。
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

        // 工作台的初始对话，用于一进来就有上下文；后续发送的消息都追加到这个集合。
        Messages = new ObservableCollection<ChatMessage>
        {
            new() { Author = "Agent", Text = "早上好。今天有 12 条待办，其中 3 条需要主管审批，2 个客户账单今天到期。" },
            new() { Author = "张客服", IsUser = true, Text = "先帮我创建河南省中医院项目，并把 9 月员工名单导入挂到这个项目下。" },
            new() { Author = "Agent", Text = "可以。请上传 9 月员工名单，我会先核对必填字段，再请你确认导入。" }
        };

        // 其余集合先建空实例，等内容通过 Select / LoadFlow 填入。
        // 关键是「先有实例，后填数据」：XAML 绑定时集合引用始终有效，界面才能持续刷新。
        FlowMessages = new ObservableCollection<ChatMessage>();
        Rows = new ObservableCollection<FlowItem>();
        Drawer = new ObservableCollection<FlowItem>();
        DrawerFooter = new ObservableCollection<FlowItem>();
    }

    // 以下六个集合都是「只读属性 + 可观察实例」：实例本身不允许替换，只允许增删元素。
    // 这样 XAML 只需绑定一次，之后 Clear/Add 都能被 ItemsControl 感知。
    public ObservableCollection<WorkbenchCard> Cards { get; }
    // 工作台对话集合。XAML 里工作台对话区直接绑它（流程页则绑 FlowMessages）。
    public ObservableCollection<ChatMessage> Messages { get; }
    // 流程页对话，切换流程时整体重填。
    public ObservableCollection<ChatMessage> FlowMessages { get; }
    // 列表页的行数据。
    public ObservableCollection<FlowItem> Rows { get; }
    // 流程页右侧抽屉的主条目。
    public ObservableCollection<FlowItem> Drawer { get; }
    // 抽屉底部分隔线以下的补充条目。
    public ObservableCollection<FlowItem> DrawerFooter { get; }

    // 五个命令都在构造函数里一次性创建，之后不再替换，绑定不会失效。
    public ICommand SelectCommand { get; }
    public ICommand CardCommand { get; }
    public ICommand OpenFlowCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand SendCommand { get; }

    // 当前导航 key。每次 Select 都会先写它，二级导航靠它做选中态。
    public string ActiveKey
    {
        get => _activeKey;
        private set => Set(ref _activeKey, value);
    }

    // 三态标记之一：工作台。它的 setter 除了赋值，还要顺带通知一串派生属性，
    // 因为 IsSection / IsFlow / TopBarTitle / ActiveMessages 全部由这几个布尔值推算而来，
    // 而普通的自动属性赋值不会触发 PropertyChanged，Avalonia 也就不会重新求值。
    public bool IsWorkbench
    {
        get => _isWorkbench;
        private set
        {
            // Set 内部会比对旧值：值没变时返回 false，这里直接退出，避免无意义的重绘。
            if (!Set(ref _isWorkbench, value)) return;
            // 值变了则手动补发四条通知：前两条是本类的另两个状态位，后两条是由状态位派生的属性。
            OnPropertyChanged(nameof(IsSection));
            OnPropertyChanged(nameof(IsFlow));
            OnPropertyChanged(nameof(TopBarTitle));
            // ActiveMessages 指向的集合换了，发一条通知让任何绑定到它的地方重新取值。
            // 当前 XAML 是分别绑 Messages / FlowMessages，这条属于防御性通知，成本可忽略。
            OnPropertyChanged(nameof(ActiveMessages));
        }
    }

    // 列表页 / 流程页标记。故意写成普通自动属性（没有 Set 包裹、没有通知），
    // 由 SetState 统一赋值后再手动发通知，这样三个状态位能在同一处对齐，不会出现两个为 true 的中间态。
    public bool IsSection { get; private set; }
    public bool IsFlow { get; private set; }

    // 顶部任务条标题：流程页显示流程名，工作台 / 列表页显示固定文案。
    // 依赖 IsFlow 与私有字段 _isWorkbench，所以状态变化时必须手动通知它。
    public string TopBarTitle => IsFlow ? FlowTitle : (_isWorkbench ? "工作台" : "工作区");

    // 当前活跃的那一路对话：工作台返回 Messages，其余状态返回 FlowMessages。
    // 两个集合分开存，切回工作台时历史消息不会丢；Send 也靠它决定把新消息追加到哪。
    // 它是计算属性，状态位一变就必须由 SetState 手动通知，否则取值方会拿到旧结果。
    public ObservableCollection<ChatMessage> ActiveMessages => _isWorkbench ? Messages : FlowMessages;

    // 列表页三件套：标题、描述、图标（图标用 Controls.Icons 里的几何数据）。
    // 三个属性都由 Select → Describe 的结果填充，所以 setter 是私有的，外部只读。
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
    // 流程页字段逐个摊成属性而不是直接暴露 FlowDefinition：
    // 这样每一项都能走 Set 单独发通知，切换流程时界面按字段精确刷新，
    // 同时 XAML 的绑定路径也更短（FlowTitle 而不是 CurrentFlow.Title）。
    private string _flowTitle = string.Empty;
    private string _flowSubtitle = string.Empty;
    private string _drawerTitle = "上下文";
    private string _flowBadge = string.Empty;
    // 徽章的画笔在加载时就换算好存下来，避免 XAML 里做值转换。
    private IBrush _flowBadgeBrush = Palette.Solid("#9BA3B7");
    private IBrush _flowBadgeBg = Palette.Alpha("#FFFFFF", 0.06);
    // 显隐开关与内容分开存：内容可以先留着，靠布尔值控制是否渲染。
    private bool _hasFlowBadge;
    private bool _hasDrawerFooter;

    // 流程页标题。它同时是 TopBarTitle 的数据源，所以改标题必须通知 TopBarTitle 一起更新。
    public string FlowTitle
    {
        get => _flowTitle;
        private set
        {
            if (!Set(ref _flowTitle, value)) return;
            // 标题是 TopBarTitle 的组成部分，改标题时必须连带通知，否则顶部任务条会停在旧文案。
            OnPropertyChanged(nameof(TopBarTitle));
        }
    }

    // 流程副标题。当前 XAML 未直接展示，预留给后续页面头部使用。
    public string FlowSubtitle
    {
        get => _flowSubtitle;
        private set => Set(ref _flowSubtitle, value);
    }

    // 右侧抽屉标题，随流程切换（如「项目字段」「执行进度」「审批节点」）。
    public string DrawerTitle
    {
        get => _drawerTitle;
        private set => Set(ref _drawerTitle, value);
    }

    // 标题旁的状态胶囊：文字 + 前后景色 + 显隐，四者从 FlowDefinition 一次性灌入。
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

    // 控制抽屉底部分隔线与底部条目是否出现；由 FlowDefinition.HasFooter 决定。
    public bool HasDrawerFooter
    {
        get => _hasDrawerFooter;
        private set => Set(ref _hasDrawerFooter, value);
    }

    // 输入框草稿。唯一一个 public set 的属性，因为 TextBox 需要双向写回；
    // 其余属性都是私有的 setter，保证界面不能直接改业务状态。
    public string Draft
    {
        get => _draft;
        set => Set(ref _draft, value);
    }

    // 当前用户信息。演示项目写死，将来接登录态时替换成服务端返回即可。
    // UserInitial 用于头像占位的一个字。
    public string UserName => "张客服";
    public string UserInitial => "张";

    /// <summary>
    /// 统一的导航入口：任何左侧二级导航点击、返回工作台都走这里，按 key 决定进入哪种状态。
    /// 优先级为：工作台 → 命中 Flows（流程页）→ 走 Describe 兜底（列表页）。
    /// </summary>
    /// <param name="key">导航项的 Key，与 Flows 的键同一套命名。</param>
    public void Select(string key)
    {
        // 先记下原始 key，高亮用的是它而不是 alias，
        // 这样点击「最近」里的条目时，高亮仍停在「最近」那一项，而内容显示对应流程。
        ActiveKey = key;

        // 分支一：回到工作台。列表页残留的标题顺手复位，避免下次进列表页时闪一下旧标题。
        if (key == "workbench")
        {
            SectionTitle = "工作台";
            SetState(workbench: true);
            return;
        }

        // 最近记录复用对应流程
        // 二级导航的「最近」只是指向已有流程的别名，本身没有独立数据，
        // 所以这里先把别名翻译成真实流程 key，再去 Flows 里查。
        var alias = key switch
        {
            "recent-1" => "settlement",
            "recent-2" => "project",
            "recent-3" => "approval",
            _ => key
        };

        // 分支二：命中流程数据 → 交给 LoadFlow 铺满流程页。
        if (Flows.TryGetValue(alias, out var flow))
        {
            LoadFlow(flow);
            return;
        }

        // 分支三：没有对应流程的常规列表页（客户、导入、账号等），由 Describe 现造一组行数据。
        var (title, desc, icon, rows) = Describe(key);
        SectionTitle = title;
        SectionDesc = desc;
        SectionIcon = icon;
        // 逐条克隆而不是直接复用 Describe 造出来的对象：那些行数据都没有挂命令，
        // 且克隆后列表页与抽屉各自持有独立实例，不会因为共享对象而互相牵连。
        Rows.Clear();
        foreach (var row in rows)
            Rows.Add(new FlowItem
            {
                Title = row.Title, Meta = row.Meta, Tag = row.Tag, Tone = row.Tone,
                // Key 原样保留（CanOpen 需要它），Command 补上页面级命令，
                // 缺这一步的话箭头不会出现、点击也没有任何反应。
                Key = row.Key, Command = OpenFlowCommand
            });
        // 两个参数都传 false，由 SetState 推导出「列表页」这一态。
        SetState(workbench: false);
    }

    /// <summary>
    /// 打开一条流程：能在 Flows 里查到就直接加载，查不到则退回 Select 走列表页逻辑。
    /// 快捷卡、抽屉条目、列表行的点击最终都汇聚到这里。
    /// </summary>
    private void OpenFlow(string key)
    {
        if (Flows.TryGetValue(key, out var flow)) LoadFlow(flow);
        else Select(key);
    }

    /// <summary>
    /// 把一份 FlowDefinition 铺到流程页：先刷标题区，再刷对话、抽屉、抽屉底部，最后切状态。
    /// 顺序很重要——先填数据再切状态，界面一次性呈现完整内容，不会先闪一个空页面。
    /// </summary>
    private void LoadFlow(FlowDefinition flow)
    {
        // 标题区：标题 / 副标题 / 抽屉标题三个文案，加上状态胶囊的文字、显隐与两支画笔。
        FlowTitle = flow.Title;
        FlowSubtitle = flow.Subtitle;
        DrawerTitle = flow.DrawerTitle;
        FlowBadge = flow.Badge;
        HasFlowBadge = flow.HasBadge;
        FlowBadgeBrush = flow.BadgeBrush;
        FlowBadgeBackground = flow.BadgeBackground;

        // 对话区：Clear + 逐条 Add，而不是给 FlowMessages 整体赋新集合。
        // 原因有两个：属性本身是只读的（外部无法替换实例）；ObservableCollection 复用一个实例，
        // XAML 绑定的订阅才始终有效，整体换实例会让已建立的绑定指向旧对象、界面不再刷新。
        // ChatMessage 不可变，直接加入即可，不必克隆。
        FlowMessages.Clear();
        foreach (var m in flow.Messages) FlowMessages.Add(m);

        // 抽屉区：这里必须克隆。演示数据里的 FlowItem.Command 为空，
        // 直接塞进去会导致抽屉条目不可点、右侧箭头也不显示；
        // 克隆时补上 OpenFlowCommand，并保留 Key 让 CanOpen 能判定通过。
        Drawer.Clear();
        foreach (var d in flow.Drawer) Drawer.Add(new FlowItem
        {
            Title = d.Title, Meta = d.Meta, Tag = d.Tag, Tone = d.Tone, Key = d.Key,
            Command = OpenFlowCommand
        });

        // 抽屉底部：纯展示条目，不参与跳转，因此直接复用原对象即可。
        DrawerFooter.Clear();
        foreach (var f in flow.Footer) DrawerFooter.Add(f);
        HasDrawerFooter = flow.HasFooter;

        // 最后一步才切状态：workbench=false + flow=true，推导出流程页。
        SetState(workbench: false, flow: true);
    }

    /// <summary>
    /// 三态收敛点：workbench / flow 两个入参推导出三个互斥布尔值，
    /// 并手动补发所有派生属性的通知。任何时候改变页面状态都应调用它，不要单独给状态位赋值。
    /// </summary>
    /// <param name="workbench">true 表示工作台态。</param>
    /// <param name="flow">true 表示流程态；与 workbench 同时为 false 时即为列表态。</param>
    private void SetState(bool workbench, bool flow = false)
    {
        // IsWorkbench 的 setter 内部已经会发一轮派生通知；
        // 下面再发一遍是为了覆盖「IsWorkbench 值没变、但 IsFlow 变了」的情况
        // （例如流程 A 直接跳到流程 B）。重复通知是幂等的，代价可以忽略。
        IsWorkbench = workbench;
        IsFlow = flow;
        // 列表态 = 既不是工作台也不是流程，这样三个 IsXxx 永远只有一个为 true。
        IsSection = !workbench && !flow;
        // IsSection / IsFlow 是自动属性，赋值不触发通知，必须手动发，
        // 否则 AdminView 里三个并列布局的 IsVisible 不会切换。
        OnPropertyChanged(nameof(IsSection));
        OnPropertyChanged(nameof(IsFlow));
        // TopBarTitle 与 ActiveMessages 是纯计算属性，同样需要手动通知才会重新求值。
        OnPropertyChanged(nameof(TopBarTitle));
        OnPropertyChanged(nameof(ActiveMessages));
    }

    /// <summary>
    /// 发送草稿：立即上屏用户消息并清空输入框，随后延迟一小段时间插入 Agent 回复，模拟「正在输入」。
    /// </summary>
    private void Send()
    {
        var text = Draft?.Trim();
        // 空内容直接丢弃，避免产生空气泡。
        if (string.IsNullOrEmpty(text)) return;

        // 先固定当前会话集合的引用：延迟回复期间用户可能切到别的页面，
        // 靠这个局部变量保证回复仍落回发起对话的那一路（工作台或某个流程）。
        var list = ActiveMessages;
        list.Add(new ChatMessage { Author = "张客服", Text = text, IsUser = true });
        Draft = string.Empty;

        // 轮转取下一条预置回复。取模保证下标永远落在数组范围内。
        var reply = Replies[_replyIndex++ % Replies.Length];
        // 用 DispatcherTimer 而不是 Thread.Sleep：前者回调在 UI 线程上执行，可安全操作
        // ObservableCollection（后台线程直接改集合会抛异常）；650ms 的间隔同时制造出
        // 「Agent 正在思考」的节奏感，不传参数也省掉了 async void 的坑。
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        timer.Tick += (_, _) =>
        {
            // 一次性定时器：先停表，避免重复触发；再追加回复。
            timer.Stop();
            list.Add(new ChatMessage { Author = "Agent", Text = reply });
        };
        timer.Start();
    }

    /// <summary>
    /// 为没有专属流程的导航 key 现造一份列表页数据。
    /// 返回值四段依次是：页面标题、页面描述、标题图标（Icons 里的几何数据）、行数据数组。
    /// 行数据里的 Key 会决定点击后跳到哪条流程，留空则表示该行不可跳转。
    /// </summary>
    /// <param name="key">二级导航 key；未匹配到任何分支时走默认的空工作区。</param>
    private static (string Title, string Desc, string Icon, FlowItem[] Rows) Describe(string key) => key switch
    {
        // 客户工作空间：一行一个客户，Key 指向该客户当前最需要处理的流程。
        "clients" => ("客户工作空间", "按客户组织项目、合同与员工档案。", Controls.Icons.Users, new[]
        {
            new FlowItem { Title = "河南省中医院", Meta = "在办项目 2 个 · 员工 128 人", Tag = "进行中", Tone = "green", Key = "project" },
            new FlowItem { Title = "清远电子", Meta = "首月结算 · 员工 64 人", Tag = "待结算", Tone = "amber", Key = "settlement" },
            new FlowItem { Title = "恒川制造", Meta = "员工增减 · 本月新增 12 人", Tag = "进行中", Tone = "green", Key = "project" },
            new FlowItem { Title = "远航科技", Meta = "政策审核 · 等待材料补充", Tag = "待处理", Tone = "accent", Key = "approval" }
        }),

        // 信息导入中心：一行一个导入批次，Tone 表示校验结果（已导入 / 待校验）。
        "import" => ("信息导入中心", "批量导入员工名单、考勤与账单数据。", Controls.Icons.Download, new[]
        {
            new FlowItem { Title = "9 月员工名单.xlsx", Meta = "张客服 · 今天 09:12 · 128 行", Tag = "已导入", Tone = "green", Key = "project" },
            new FlowItem { Title = "8 月考勤汇总.xlsx", Meta = "张客服 · 昨天 17:40 · 126 行", Tag = "已导入", Tone = "green", Key = "kpi" },
            new FlowItem { Title = "清远电子 账单.csv", Meta = "系统 · 昨天 14:02 · 待校验", Tag = "待校验", Tone = "amber", Key = "settlement" }
        }),

        // 账号与权限：纯展示行，刻意不填 Key，因此不会出现右侧箭头、点击也不跳转。
        "account" => ("账号与权限", "按部门与客户范围分配访问权限。", Controls.Icons.Users, new[]
        {
            new FlowItem { Title = "张客服 · 客服", Meta = "部门：客户成功 · 全部客户", Tag = "全部权限", Tone = "accent" },
            new FlowItem { Title = "李锐 · 销售", Meta = "部门：销售 · 客户：远航科技", Tag = "受限", Tone = "amber" },
            new FlowItem { Title = "王敏 · 财务", Meta = "部门：财务 · 基础结算", Tag = "受限", Tone = "amber" }
        }),

        // 兜底分支：未知 key 不报错，给一张空列表 + 引导文案，保证导航永远有页面可看。
        _ => ("工作区", "从常用入口开始，也可以直接在下方对话中交办工作。", Controls.Icons.Grid, Array.Empty<FlowItem>())
    };
}
