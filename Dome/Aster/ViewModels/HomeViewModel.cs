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
/// 视图侧用 x:DataType="vm:HomeViewModel" 声明绑定上下文类型，为 {Binding} 提供类型推导；
/// 本项目未开启编译绑定，所以它只是类型提示，不构成属性名的编译期校验。
/// </summary>
public sealed class HomeViewModel : ObservableObject
{
    // 【先回答一个必然会问的问题：为什么继承了 ObservableObject，却一次通知都用不上】
    // 本类的公开成员一共 12 个，全部是只读的：
    //   · 7 个 string + 1 个 double 是 get-only 自动属性，值在类型初始化那一刻就定死了；
    //   · 3 个 ObservableCollection 与 1 个 double[]（Rhythm）只在字段初始化器里填充一次，
    //     运行期没有任何 Add / Remove / Clear 的入口。
    // 通篇没有 setter、没有 ICommand、没有一行 OnPropertyChanged 调用 ——
    // 也就是说 INotifyPropertyChanged 这条链路在本类里永远不会被触发一次，绑定只会读到「第一帧的值」。
    // 继承基类的实际收益只剩下「与工程里其它 VM 保持统一的类型签名」（将来真要加可写属性时不必改声明）；
    // 若想精简，去掉基类、把集合换成普通 List，界面表现完全一样。
    // 对照 MusicViewModel：那边 Position 每秒 +1、IsPlaying 随时翻转，才是真正吃到了 Set() 的通知能力。
    //
    // 【本类与视图的分工】HomeView.axaml 里没有任何 Command 绑定，也没有 code-behind 逻辑，
    // 所以这一页是「一份数据 + 一份模板」的纯静态渲染：VM 只负责给出文案与颜色，交互一概没有。

    // ---------- 问候区：问候语 + 用户名 + 一句状态副标题 ----------
    // 三条都是常量，页面刷新时不会变化；姓名单独拆成一个属性，是为了在 XAML 里只给它上渐变。
    // 界面落点（HomeView.axaml 最外层问候 Grid 的左侧 StackPanel）：
    //   Greeting → 第一个 23px SemiBold TextBlock，Foreground 取 StaticResource Text0（静态浅白）
    //   UserName → 紧邻的第二个 23px TextBlock，Foreground 换成 8FA8FF → A78BFA → 3DD6E8 横向渐变
    //   Subtitle → 其下 12.5px Text2 的副标题，Margin 0,7,0,0 拉开与标题的距离
    public string Greeting { get; } = "下午好，";
    public string UserName { get; } = "林深";
    public string Subtitle { get; } = "今天写了 3 篇文档，听了 42 分钟音乐 — 继续保持";

    // ---------- 指标卡：HomeView 顶部 UniformGrid Columns=4 的数据源，顺序即卡片顺序 ----------
    // 卡片配色不在这里决定：DeltaKind（up / warn / flat，flat 落到默认灰）由 KpiItem 换算成胶囊底色与文字色。
    //
    // 【界面落点】HomeView.axaml 里唯一的 ItemsControl（ItemsSource="{Binding Kpis}"）：
    //   容器被换成 UniformGrid Columns=4，于是「集合里有几个元素」直接等于「横向排几张卡」，
    //   元素先后顺序就是卡片从左到右的顺序；窗口变窄时四张卡一起变窄，不会折行。
    // 【模板取走哪几个字段】每张卡在 KpiItem 的 DataTemplate 里被分成四段读：
    //   Title                                    → 卡片顶部 11.5px Text2 灰标题
    //   Value + Unit + Tail                      → 同一行横向 StackPanel 的三段数字排版
    //   Delta                                    → 底部胶囊里的文字
    //   ValueBrush / DeltaBrush / DeltaBackground → 大数字色、胶囊文字色、胶囊底色（三个都由 KpiItem 算好）
    // 【为什么用 ObservableCollection】本页只填充一次、之后从不改动，用 List 也完全可以；
    //   选它只是与工程内其它集合保持类型一致，将来真要动态加卡时不用改绑定类型。
    // 【四张卡在演示三种排版】
    //   第 1 张：只有 Value（128），Unit / Tail 为 null → 模板里两个附属 TextBlock 的 IsVisible 为 false，只剩一个大数字；
    //   第 2 张：Value=32、Unit=h、Tail=18m → 26px 数字 + 13px 单位 + 12px 尾注，三级字号递减；
    //   第 3 张：Value=12、无单位 → 靠 ValueBrushOverride 把大数字单独染成琥珀色，用来替代「加个警示图标」的强调手法；
    //   第 4 张：Value=6.8、Unit=GB、Tail="/ 20 GB" → 尾注用「/ 容量」的写法表达占比语境。
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
    //
    // 【界面落点】主栅格左列第一张卡（card 类 Border）里那个 ItemsControl，ItemsSource 直接绑 RecentDocs。
    //   只有 4 条，用 ItemsControl 而不上 ListBox：不需要虚拟化，也不需要单选/键盘导航那套逻辑。
    // 【模板取走哪几个字段】每行 Auto,*,Auto 三列：
    //   IconBackground + IconBrush → 行首 30x30 圆角块的底色与其中 14px 图标的颜色（都由 Tone 换算，见 Items.cs）
    //   Name                      → 主标题 12.5px Text0
    //   Meta                      → 副标题 11px Text2（"Notes / 产品 · 2,148 字" 是拼好的单串，不在这里格式化）
    //   Time                      → 右侧 11px Text2 相对时间
    // Tone 是字符串而不是枚举，拼错（比如写成 "viloet"）不会报错，而是静默落到默认 accent 分支。
    public ObservableCollection<RecentDocItem> RecentDocs { get; } = new()
    {
        new RecentDocItem { Name = "Aster 架构设计", Meta = "Notes / 产品 · 2,148 字", Time = "3 分钟前" },
        new RecentDocItem { Name = "Q4 产品路线图评审", Meta = "Notes / 会议 · 1,032 字", Time = "1 小时前", Tone = "violet" },
        new RecentDocItem { Name = "2026-10-20 工作日志", Meta = "Journal / 日常 · 486 字", Time = "今天 09:12", Tone = "cyan" },
        new RecentDocItem { Name = "Avalonia 跨平台踩坑记录", Meta = "Notes / 技术 · 3,720 字", Time = "昨天" }
    };

    // ---------- 快捷操作：左列 2x2 网格，Icon 存的是 Controls.Icons 里的 SVG path 字符串 ----------
    // 该字符串直接绑到自绘 Icon 控件的 Data，所以这里不需要命令，四个按钮目前点击无行为。
    //
    // 【界面落点】主栅格左列第二张卡里那个 ItemsControl，容器是 UniformGrid Columns=2，
    //   四条数据正好铺成 2x2；按钮的 Margin 0,0,9,9 同时充当列间距与行间距（最后一行右侧会多 9，被卡片裁剪吃掉）。
    // 【模板取走哪几个字段】
    //   Icon  → 14px 图标，其 Foreground 在模板里写死 StaticResource Accent，不随条目变化；
    //   Title → 12px Text1 文案。
    // QuickAction 类型本身没有 Command 字段，模板里的 Button 也没有 Command 绑定 ——
    // 四个按钮只有默认的悬停/按下反馈，点下去不产生任何业务动作。要接功能，应在这里加 Command 与对应筛选键。
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
    //
    // 【界面落点】主栅格右列第一张卡「继续播放」里那行 Auto,* 的 Grid，右侧 StackPanel 从下往上读四个属性：
    //   NowPlayingTitle    → 13.5px SemiBold 第一行，超长走 TextTrimming 省略而不撑宽卡片
    //   NowPlayingArtist   → 11.5px Text2 第二行
    //   NowPlayingPosition → 进度条左端 10px 时间（"1:47"），Width 未定、靠左对齐
    //   NowPlayingDuration → 进度条右端 10px 时间（"4:12"），同一列里 HorizontalAlignment=Right
    // NowPlayingProgress 是"应有的百分比（42%）"这一意图的占位，界面目前用固定宽度 42px 的紫色 Border 假装，
    // 两个 42 只是巧合相同（一个是百分数、一个是像素），并不存在换算关系 —— 这是本页最容易误读的一处。
    public string NowPlayingTitle { get; } = "Midnight Architecture";
    public string NowPlayingArtist { get; } = "Aurora Field · Nocturne";
    public string NowPlayingPosition { get; } = "1:47";
    public string NowPlayingDuration { get; } = "4:12";
    public double NowPlayingProgress { get; } = 42;

    // ---------- 今日节奏：Sparkline 的折线点集，13 个值按小时采样 ----------
    // 点集在 Sparkline 内部按 min/max 自行归一化，所以只需保持相对高低关系，量纲无所谓。
    //
    // 【界面落点】主栅格右列第二张卡里的自绘控件：
    //   <ctrl:Sparkline Points="{Binding Rhythm}" Height="78" Stroke="{StaticResource Accent}" />
    // PointsProperty 注册为 StyledProperty<IReadOnlyList<double>?> 并挂了 AffectsRender，
    // 所以绑定一旦赋值，控件就会在 Measure/Arrange 之后重绘，这里不需要任何手动刷新。
    // 【为什么不用预处理】Sparkline.Render 先扫一遍求 min/max，把 [min,max] 线性压到 [0,1] 再映射到绘制区高度，
    // 于是「给绝对秒数」还是「给 0~100 的分数」画出来完全一样；x 按 index/(Count-1) 均分，
    // 13 个点会自动铺满 78 高的画布宽度，采样间隔不等距也不会错位。
    // 唯一要保证的是「至少 2 个点」，少于 2 个 Sparkline 直接 return 什么都不画。
    // 下面这串数字是手工编的曲线：清晨高、中午塌到底、傍晚略回升，用来演示折线与渐变面积的形态。
    public IReadOnlyList<double> Rhythm { get; } = new double[]
    {
        60, 52, 56, 38, 44, 24, 30, 14, 26, 18, 34, 22, 30
    };
}
