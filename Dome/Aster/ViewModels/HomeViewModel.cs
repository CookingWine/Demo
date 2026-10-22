// ============================================================================
// HomeViewModel.cs —— 「首页」个人工作台的视图模型
// 职责：给 HomeView 提供问候语、4 张 KPI 卡、最近编辑、快捷操作、继续播放、今日节奏的展示数据。
// 位置：由 MainWindowViewModel 构造时创建（MainWindow.Home），但一级导航目前只保留「管理 / 设置」，
//       本页当前未挂到一级导航，属于保留待恢复的模块。
// 数据：全部是字段/属性初始化器里的硬编码假数据，不访问数据库、网络或本地文件。
// 坑：集合元素（KpiItem / RecentDocItem / QuickAction）是 init-only 且不实现属性通知，
//     运行期改元素属性不会刷新界面；要变化只能整体替换集合里的元素。
// ============================================================================
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Aster.ViewModels;

/// <summary>
/// 首页工作台的只读数据源：集合只在初始化时填充一次，页面本身没有命令，纯展示。
/// 视图侧用 x:DataType="vm:HomeViewModel" 做编译期绑定校验。
/// </summary>
public sealed class HomeViewModel : ObservableObject
{
    // ---------- 问候区：问候语 + 用户名 + 一句状态副标题 ----------
    // 三条都是常量，页面刷新时不会变化；姓名单独拆成一个属性，是为了在 XAML 里只给它上渐变。
    public string Greeting { get; } = "下午好，";
    public string UserName { get; } = "林深";
    public string Subtitle { get; } = "今天写了 3 篇文档，听了 42 分钟音乐 — 继续保持";

    // ---------- 指标卡：HomeView 顶部 UniformGrid Columns=4 的数据源，顺序即卡片顺序 ----------
    // 卡片配色不在这里决定：DeltaKind（up / warn / flat，flat 落到默认灰）由 KpiItem 换算成胶囊底色与文字色。
    public ObservableCollection<KpiItem> Kpis { get; } = new()
    {
        new KpiItem { Title = "文档总数", Value = "128", Delta = "↑ 12  本周新增", DeltaKind = "up" },
        new KpiItem { Title = "总写作时长", Value = "32", Unit = "h", Tail = "18m", Delta = "↑ 8.4%  较上周", DeltaKind = "up" },
        // 第三张卡走 warn 语义：胶囊变琥珀色，再用 ValueBrushOverride 把 26px 的大数字一起染色。
        new KpiItem
        {
            Title = "待处理事项", Value = "12", Delta = "3 项逾期", DeltaKind = "warn",
            ValueBrushOverride = Palette.Solid(Palette.AmberColor)
        },
        new KpiItem { Title = "存储占用", Value = "6.8", Unit = "GB", Tail = "/ 20 GB", Delta = "34% 已使用", DeltaKind = "flat" }
    };

    // ---------- 最近编辑：左列卡片列表，Tone 只控制行首图标的底色/图标色，不参与排序 ----------
    // Tone 缺省为 accent，另有 violet / cyan；时间列是写死的相对时间文案，不是 DateTime。
    public ObservableCollection<RecentDocItem> RecentDocs { get; } = new()
    {
        new RecentDocItem { Name = "Aster 架构设计", Meta = "Notes / 产品 · 2,148 字", Time = "3 分钟前" },
        new RecentDocItem { Name = "Q4 产品路线图评审", Meta = "Notes / 会议 · 1,032 字", Time = "1 小时前", Tone = "violet" },
        new RecentDocItem { Name = "2026-10-20 工作日志", Meta = "Journal / 日常 · 486 字", Time = "今天 09:12", Tone = "cyan" },
        new RecentDocItem { Name = "Avalonia 跨平台踩坑记录", Meta = "Notes / 技术 · 3,720 字", Time = "昨天" }
    };

    // ---------- 快捷操作：左列 2x2 网格，Icon 存的是 Controls.Icons 里的 SVG path 字符串 ----------
    // 该字符串直接绑到自绘 Icon 控件的 Data，所以这里不需要命令，四个按钮目前点击无行为。
    public ObservableCollection<QuickAction> QuickActions { get; } = new()
    {
        new QuickAction { Title = "新建 Markdown", Icon = Controls.Icons.Plus },
        new QuickAction { Title = "插入模板", Icon = Controls.Icons.DocText },
        new QuickAction { Title = "导入本地音乐", Icon = Controls.Icons.Music },
        new QuickAction { Title = "导出报告", Icon = Controls.Icons.Download }
    };

    // ---------- 继续播放：右列卡片的三段文案 + 一个百分比 ----------
    // 注意 NowPlayingProgress=42 目前没有任何绑定使用：卡片里的进度条是一段固定 Width=42 的 Border，
    // 想让它动起来，得把那段 Border 的宽度（或换成 TrackBar）绑到这个属性上。
    // 这里的「正在播放」与 MusicViewModel 无关，是首页自己的静态示例数据。
    public string NowPlayingTitle { get; } = "Midnight Architecture";
    public string NowPlayingArtist { get; } = "Aurora Field · Nocturne";
    public string NowPlayingPosition { get; } = "1:47";
    public string NowPlayingDuration { get; } = "4:12";
    public double NowPlayingProgress { get; } = 42;

    // ---------- 今日节奏：Sparkline 的折线点集，13 个值按小时采样 ----------
    // 点集在 Sparkline 内部按 min/max 自行归一化，所以只需保持相对高低关系，量纲无所谓。
    public IReadOnlyList<double> Rhythm { get; } = new double[]
    {
        60, 52, 56, 38, 44, 24, 30, 14, 26, 18, 34, 22, 30
    };
}
