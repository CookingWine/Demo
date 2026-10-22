// ============================================================================
// MusicViewModel.cs —— 音乐模块的视图模型（曲库 + 播放控制 + 可视化 + 播放队列）
// 职责：维护曲库全集、当前曲、播放进度、音量、筛选页签，并对外暴露窗口绑定的命令。
// 位置：当前未挂到一级导航（导航只保留「管理 / 设置」），属于未启用的保留模块；
//       代码里也没有任何地方 new MusicViewModel()，只有 ShellView 的 DataTemplate 在等它。
// 数据：8 首写死的示例曲；没有真实音频解码，播放进度由 DispatcherTimer 每秒 +1 模拟。
// 坑：Position 单位是秒、Progress 是百分比，两者用途不同不能混用；切换曲目后要手动通知依赖 _current 的属性。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Aster.ViewModels;

/// <summary>
/// 曲库里的单首歌曲：元数据 + 封面画刷 + 播放状态 + 点击命令。
/// init-only 字段初始化后不再变化，只有 IsSelected / IsPlaying 是可变状态（列表高亮与跳动指示器用）。
/// </summary>
public sealed class SongItem : SelectableItem
{
    // 【界面落点】左栏曲库列表（MusicView 的 ItemsControl，ItemsSource 绑 VisibleSongs），
    // 每一条 SongItem 渲染成一行 Button.song，模板按下列字段取值：
    //   Cover     → 34x34 圆角方块 Background（直接是 IBrush，省掉值转换器）
    //   Title     → 12px 第一行，超长 TextTrimming 省略
    //   Artist    → 10.5px Text2 第二行
    //   IsSelected→ 驱动 Classes.selected（选中底色 #216C8BFF 由样式选择器打在模板内的 Border#Bg 上）
    //   IsPlaying → 控制右侧那格：true 显示 EqIndicator 跳动条，false 显示 Duration 文案
    //   Command   → 与 CommandParameter="{Binding}" 配合，把"被点的这一条"回传给 SelectSongCommand
    // 也就是说一个 SongItem 身上的字段分两类：展示字段（init-only，永不刷新）
    // 与状态字段（IsSelected / IsPlaying，由 MusicViewModel 集中写，靠 Set() 逐条通知单行刷新）。
    // ---------- 展示字段：全部 init-only，构造后不再变化 ----------
    // Duration 是给人看的 "4:12" 文案，Seconds 是给计时器比较的真实秒数，两者要成对维护、指向同一首曲。
    // Cover 直接是画刷（两色渐变），视图里 Background="{Binding Cover}" 即可，省掉值转换器。
    public string Title { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public string Duration { get; init; } = string.Empty;
    public double Seconds { get; init; }
    public IBrush Cover { get; init; } = Palette.Solid("#2B2F55");
    // 三个布尔标记同时充当筛选依据：IsFavorite / IsRecent 被 ApplyFilter 的「收藏 / 最近」页签使用；
    // IsLocal 目前没有任何界面表现（没有本地标记角标），只保留作数据位。
    public bool IsFavorite { get; init; }
    public bool IsRecent { get; init; }
    public bool IsLocal { get; init; }
    public ICommand Command { get; init; } = null!;

    // ---------- 运行期状态：IsPlaying 控制列表右侧的三格跳动指示器 ----------
    // 它由 MusicViewModel.IsPlaying 的 setter 统一写入，保证「当前曲」永远只有一首在跳。
    // setter 里 Set() 相等即返回：同一行重复赋同样的值不会触发无意义的重绘
    // （这很关键，因为 Tick 每秒都会走一遍 IsPlaying，若每次都通知就等于每秒全列表重绘一次）。
    // IsPlaying 变化只会影响这一行：绑它的两个控件是互斥的 EqIndicator / 时长 TextBlock。
    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set => Set(ref _isPlaying, value);
    }

    // 选中态与「当前曲」是同一件事，这里给 XAML 一个语义更清楚的别名（IsSelected 来自 SelectableItem）。
    // 提醒：这个别名目前没有被任何 XAML 使用（列表里绑的是 IsSelected），属于预留语义。
    // 它是计算属性、没有后备字段，IsSelected 变化时不会连带发出 IsCurrent 的通知，直接绑它会拿不到更新。
    public bool IsCurrent => IsSelected;
}

/// <summary>
/// 音乐播放器视图模型：假播放实现（DispatcherTimer 每秒推进一秒），不依赖任何音频库。
/// 左栏曲库与筛选、中栏唱片/歌词/控制、右栏可视化与队列的状态都集中在这里。
/// </summary>
public sealed class MusicViewModel : ObservableObject
{
    // 【这个 VM 管着界面的哪几块】对应 MusicView.axaml 的三栏：
    //   左栏（286）：LibraryTitle / LibrarySubtitle 两行标题、Tabs 页签、VisibleSongs 曲库列表；
    //   中栏（*）  ：Title / Artist / 三行 LyricsLine、Position + DurationSeconds + PositionText / DurationText 进度、
    //               IsPlaying 驱动的 PlayIcon 与 SpinningDisc 转动，ShuffleBrush / RepeatBrush 两个开关配色；
    //   右栏（224）：IsPlaying 驱动 AudioBars 频谱、Queue 队列列表（其余码率参数是 XAML 里的静态文案）。
    // 【状态都在这里的原因】播放进度、当前曲、音量都放在 VM，视图可以被随时销毁重建（切模块不丢进度），
    // 代价是 VM 里跑着一个永不停止的 DispatcherTimer，见构造函数步骤 1 的说明。
    // 【通知分三类】① Set() 自动通知自身；② 派生属性（Title / PositionText / Progress ...）必须手动 OnPropertyChanged；
    // ③ 集合变动（VisibleSongs.Clear/Add）靠 ObservableCollection 的 CollectionChanged 自动刷新，不用通知属性。

    // ---------- 播放状态字段 ----------
    // _current 为 null 表示还没选过曲；_isPlaying 默认 true，进页面就是「正在播放」的观感。
    // _isRepeat 目前只驱动 RepeatBrush 换色：Next 到末尾一律回到第一首，等价于列表循环。
    private readonly DispatcherTimer _timer;
    private SongItem? _current;
    private bool _isPlaying = true;
    private bool _isShuffle;
    private bool _isRepeat = true;
    private double _position;
    private double _volume = 62;

    // 【六步初始化，顺序不能重排】
    //   步骤 1 · 建计时器并启动：lambda 只捕获字段，此刻 _current 尚为 null，Tick 会早返回，所以安全；
    //   步骤 2 · 建命令：命令体引用的都是方法（ApplyFilter / SelectSong / Next / TogglePlay），不依赖集合已存在；
    //   步骤 3 · 建曲库 AllSongs：每首歌的 Command 都指向步骤 2 的 SelectSongCommand，因此必须排在 2 之后；
    //   步骤 4 · 建可见列表 VisibleSongs 与队列 Queue：两者都从 AllSongs 派生，必须排在 3 之后；
    //   步骤 5 · 建页签 Tabs：复用步骤 2 的 FilterCommand；
    //   步骤 6 · 默认选第一首：要读 AllSongs[0]，且会把 IsPlaying 置 true，只能放最后。
    // 反过来（比如把 6 提到 3 之前）会先访问还没赋值的 AllSongs，直接 NullReferenceException；把 1 放到 6 之后虽然不报错，
    // 但进入页面的第一秒不会有进度推进，观感上像卡了一下。
    public MusicViewModel()
    {
        // ---------- 「假播放」的核心：DispatcherTimer 每秒 Tick 一次 ----------
        // 链路：IsPlaying 且存在当前曲 -> Position + 1 秒 -> 到达该曲 Seconds 后归零并自动 Next()。
        // Position 的 setter 会连带通知 PositionText / Progress，所以时间文案与进度条自动跟着走。
        // 坑：没有音频解码、也没有播放位置校准 —— 拖动进度条只是改数字，声音不会真的跳转。
        // 坑：timer 在构造时就 Start 且从不 Stop，视图销毁后仍在跑（因未挂导航，暂未暴露问题）。
        // 步骤 1：建计时器。Interval=1s 是"人类读秒"的最小颗粒，不是音频时钟的采样率；
        //         它跑在 UI 线程的 Dispatcher 上，所以 Tick 里可以直接改绑定属性，不需要 Dispatcher.UIThread.Post。
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            // Tick 每秒跑一次，单次处理只有几行赋值，耗时远小于 1s，所以不需要防重入标记。
            // 两条早返回各有分工：
            //   !IsPlaying  → 暂停时什么都不做，于是"停在当前秒"，恢复播放后从原处继续，而不是跳走；
            //   _current is null → 构造期计时器已启动但还没选曲，此时没有曲长可比，跳过等下一次。
            // 这里 IsPlaying 走属性 getter、_current 直接读字段，都发生在 UI 线程，不存在并发问题。
            if (!IsPlaying || _current is null) return;
            // Position += 1 会先过 setter 里的 Math.Clamp(value, 0, 当前曲长)：
            // 所以哪怕用户刚把进度条拖到最后一秒，下一拍也只会停在 Seconds 上而不会越界。
            Position += 1;
            // 到点判定用 >= 而不是 ==：Clamp 之后 Position 会精确等于 Seconds，
            // 但拖动可能把人送到任意位置，>= 能覆盖"停在末尾"的所有入径。
            if (Position >= _current.Seconds)
            {
                // 先把进度归零，再换曲。SelectSong 内部其实也会写一次 Position=0，
                // 这里是显式兜底：让"归零"发生在换曲之前，不依赖 SelectSong 的实现顺序。
                Position = 0;
                // Next() 内部会走 SelectSong → 换 _current、重置进度、通知 Title/Artist 等派生属性，
                // 所以这一拍结束时界面已经完整切到下一首，不需要在 Tick 里额外通知任何东西。
                Next();
            }
        };
        // 步骤 1 收尾。这里要如实记住三件事，"假播放"就是这个意思：
        //   1) 没有解码：工程里没有任何音频库，所谓"播放"只是每秒给 Position 加 1；
        //   2) 不校准时钟：Tick 只保证"大约每秒一次"，不累计校准，系统繁忙或休眠后会漂；
        //   3) 切窗口照常走：DispatcherTimer 挂在 UI 消息循环上，窗口最小化、模块被切走都继续推进。
        // 另外注意它从头到尾没有 Stop()，所谓"暂停"只是让 Tick 早返回，计时器本身一直活着。
        _timer.Start();

        // ---------- 命令：全是同步 RelayCommand，没有 CanExecute 约束 ----------
        // 页签与歌曲项共用 FilterCommand / SelectSongCommand，靠 CommandParameter 区分目标。
        // 坑：XAML 里 CommandParameter 传的是 SegmentItem 对象，而这里用 o as string 取值，
        //     所以「收藏 / 最近 / 专辑」点下去实际都落到兜底分支「全部」，筛选并未真正生效。
        //
        // 步骤 2：建命令。七个命令都不带 CanExecute，因此本期没有任何按钮会因状态被禁用
        // （例如列表里第一首的"上一首"依然可点，会绕回最后一首）。
        // 与 XAML 的对应关系：
        //   FilterCommand        ← 左栏四个页签 Button（CommandParameter 传的是 SegmentItem，见下方坑）
        //   SelectSongCommand    ← 每一行曲目（通过 SongItem.Command 转发，参数是被点的 SongItem）
        //   TogglePlayCommand    ← 中栏 46px 主播放按钮
        //   PrevCommand / NextCommand ← 中栏左右两个箭头
        //   ToggleShuffleCommand / ToggleRepeatCommand ← 中栏随机 / 循环两个图标按钮
        FilterCommand = new RelayCommand(o => ApplyFilter(o as string ?? "全部"));
        // 把这条失败链完整走一遍（只是记录现状，不动代码）：
        //   ① 页签模板里 CommandParameter="{Binding}"，拿到的就是 SegmentItem 本身；
        //   ② o as string 得到 null（SegmentItem 不是字符串类型），?? "全部" 兜底；
        //   ③ 于是点「收藏」实际执行的是 ApplyFilter("全部")，VisibleSongs 被重建为全集 —— 看起来"点了没反应"；
        //   ④ 页签高亮绑的是 SegmentItem.IsSelected，除构造时把 Tabs[0] 置 true 之外没有第二处写它，
        //      所以高亮永远停在「全部」，也不会跟着点击移动。
        // 还有一个容易被忽略的副作用：每次点击都会 Clear 再 Add 八次，ObservableCollection 逐条发通知，
        // 列表行控件被销毁重建，行内 EqIndicator 的动画相位也跟着归零、从头开始跳。
        // 歌曲项点击：参数就是被点的 SongItem（XAML 里 CommandParameter="{Binding}"）。
        // o is SongItem 的模式匹配顺便当了类型校验：参数为 null 或别的类型时静默忽略，
        // 不会把 null 传进 SelectSong 触发空引用。
        SelectSongCommand = new RelayCommand(o =>
        {
            if (o is SongItem song) SelectSong(song);
        });
        TogglePlayCommand = new RelayCommand(TogglePlay);
        NextCommand = new RelayCommand(Next);
        PrevCommand = new RelayCommand(Prev);
        // 随机 / 循环开关只翻转布尔值，再手动 OnPropertyChanged(ShuffleBrush / RepeatBrush)：
        // 画刷是没有后备字段的计算属性，Set() 的通知覆盖不到，必须显式补一次，否则颜色不刷新。
        // 两行的顺序不能反过来：先改值、再发通知，通知到达时 getter 才能算出新颜色；
        // 若先通知后赋值，绑定会在旧值上重算一次，视觉上要等下一次通知才变。
        // 这两个字段在整个工程里只有这两处写入口，所以不必担心别处绕过通知直接改字段。
        ToggleShuffleCommand = new RelayCommand(() =>
        {
            IsShuffle = !IsShuffle;
            OnPropertyChanged(nameof(ShuffleBrush));
        });
        ToggleRepeatCommand = new RelayCommand(() =>
        {
            IsRepeat = !IsRepeat;
            OnPropertyChanged(nameof(RepeatBrush));
        });

        // ---------- 曲库数据：8 首写死的示例曲，Seconds 与 Duration 文案一一对应 ----------
        // Cover 是两色渐变画刷，用作左栏列表的 34x34 方块封面；三个布尔标记供筛选使用。
        //
        // 步骤 3：建曲库。这是全集，之后任何筛选都只在它上面做投影，绝不删改它本身。
        // 两条不变量要人工维护：
        //   · Duration 与 Seconds 指向同一首歌的同一段时长（前者给人看 "4:12"，后者给计时器比较 252）；
        //   · 每首的 Command 都指向步骤 2 建好的 SelectSongCommand，XAML 靠 CommandParameter="{Binding}" 把自己传回去。
        // 曲目对象是 init-only 的，唯独 IsSelected / IsPlaying 会随播放变化，所以它们才需要有通知能力。
        AllSongs = new List<SongItem>
        {
            new() { Title = "Midnight Architecture", Artist = "Aurora Field", Duration = "4:12", Seconds = 252, IsFavorite = true, IsRecent = true,
                Cover = Palette.Gradient("#2B2F55", "#8C5AA8"), Command = SelectSongCommand },
            new() { Title = "Paper Lanterns", Artist = "Hana Ito", Duration = "3:48", Seconds = 228, IsRecent = true,
                Cover = Palette.Gradient("#2A3A3F", "#7FC8A9"), Command = SelectSongCommand },
            new() { Title = "Velvet Static", Artist = "Nocturne Club", Duration = "4:21", Seconds = 261, IsFavorite = true,
                Cover = Palette.Gradient("#3A2436", "#C86A7A"), Command = SelectSongCommand },
            new() { Title = "Signal Drift", Artist = "Kite & Rope", Duration = "5:02", Seconds = 302,
                Cover = Palette.Gradient("#2A2F45", "#8290D0"), Command = SelectSongCommand },
            new() { Title = "Golden Hour Loop", Artist = "Mira Vale", Duration = "3:12", Seconds = 192, IsLocal = true,
                Cover = Palette.Gradient("#3D2E1F", "#C9A15C"), Command = SelectSongCommand },
            new() { Title = "Deep Current", Artist = "Aurora Field", Duration = "6:18", Seconds = 378, IsRecent = true,
                Cover = Palette.Gradient("#1F3339", "#5FC6D6"), Command = SelectSongCommand },
            new() { Title = "Glass Corridor", Artist = "Echo Room", Duration = "4:05", Seconds = 245,
                Cover = Palette.Gradient("#2F2233", "#9B6FB5"), Command = SelectSongCommand },
            new() { Title = "Slow Orbit", Artist = "Mira Vale", Duration = "3:56", Seconds = 236, IsLocal = true, IsFavorite = true,
                Cover = Palette.Gradient("#33262B", "#B57F8B"), Command = SelectSongCommand }
        };

        // VisibleSongs 才是左栏真正渲染的集合：切页签时只重建它，AllSongs 始终保留全集。
        // 步骤 4：建可见列表与队列。
        // 注意 VisibleSongs 与 AllSongs 装的是同一批 SongItem 实例（浅拷贝引用，不是克隆），
        // 所以改某一首的 IsSelected / IsPlaying 时，两边看到的是同一个对象 —— 这正是"筛掉再切回来，选中态还在"的原因。
        VisibleSongs = new ObservableCollection<SongItem>(AllSongs);
        // 队列固定取第 2~7 首做「接下来 6 首」的静态展示，与真实播放顺序（含随机）没有联动。
        // 拆解：Skip(1) 丢掉当前默认曲，Take(6) 取六首；Select 的 i 从 0 起，所以 Index 用 (i+1) 写成 1~6。
        // PlainRow 只有序号与歌名两个字符串，没有 Command、也没有歌曲引用，因此队列行点了不会切歌。
        Queue = new ObservableCollection<PlainRow>(AllSongs.Skip(1).Take(6)
            .Select((s, i) => new PlainRow { Index = (i + 1).ToString(), Text = s.Title }));

        // 页签复用同一个 FilterCommand，Label 既是显示文案也是筛选键，改文案要同步改 ApplyFilter 的 switch。
        // 步骤 5：建页签。SegmentItem 的构造函数只有两个参数：Label（文案）与 Command（点击命令）。
        // 四个条目共用同一个命令实例，目标靠 CommandParameter 区分 —— 但那个参数实际传不进来，详见 ApplyFilter 的说明。
        Tabs = new ObservableCollection<SegmentItem>
        {
            new("全部", FilterCommand),
            new("收藏", FilterCommand),
            new("最近", FilterCommand),
            new("专辑", FilterCommand)
        };
        // 默认选中「全部」，与末尾的 SelectSong(AllSongs[0]) 一起构成进入页面时的初始状态。
        // 这里用下标 0 而不是按 Label 查：顺序由上面那个集合初始化器保证，改顺序要记得同步这一行。
        Tabs[0].IsSelected = true;

        // 步骤 6：默认选第一首。这一步之后 _current 非空、IsPlaying=true、Position=0，
        // 于是计时器的心跳从下一拍开始真正推进，各栏绑定的 Title / Artist / 播放图标也第一次拿到真值。
        SelectSong(AllSongs[0]);
    }

    // ---------- 对外暴露的集合（XAML 只读绑定） ----------
    // 绑定位置速查（都在 MusicView.axaml 里）：
    //   VisibleSongs → 左栏 ScrollViewer 内那个 ItemsControl（曲库列表，行模板是 Button.song）
    //   Tabs         → 左栏第 1 行的横向 ItemsControl（页签，行模板是 Button.tab）
    //   Queue        → 右栏底部 ScrollViewer 内那个 ItemsControl（队列，行模板是两列 Grid）
    //   AllSongs     → 没有任何 XAML 直接绑它，只在 SelectSong / Next / Prev / ApplyFilter 内部当全集用
    // 四个集合都是 get-only 属性：引用永不替换，界面靠 CollectionChanged 感知增删，不靠属性通知。
    public List<SongItem> AllSongs { get; }
    // 左栏列表数据源：由 ApplyFilter 重建，内容可能只是 AllSongs 的一个子集。
    public ObservableCollection<SongItem> VisibleSongs { get; }
    // 右栏「播放队列」数据源：PlainRow 只有序号与歌名，纯展示，点击不会切歌。
    public ObservableCollection<PlainRow> Queue { get; }
    // 顶部筛选页签：SegmentItem 自带 IsSelected，用于 Button.tab 的 selected 类高亮。
    public ObservableCollection<SegmentItem> Tabs { get; }

    // ---------- 对外暴露的命令：与 MusicView 的按钮一一对应 ----------
    public ICommand FilterCommand { get; }
    // 点歌：所有 SongItem.Command 都指向它，参数是歌曲本身。
    public ICommand SelectSongCommand { get; }
    // 播放/暂停：只翻转 IsPlaying，图标与各处动画的联动都在 IsPlaying 的 setter 里。
    public ICommand TogglePlayCommand { get; }
    // 下一首：顺序 +1 或随机取一首，见下方 Next()。
    public ICommand NextCommand { get; }
    // 上一首：只做顺序回退，不受随机开关影响。
    public ICommand PrevCommand { get; }
    // 随机 / 循环开关：切换后由命令自己手动通知对应的 *Brush 计算属性。
    public ICommand ToggleShuffleCommand { get; }
    public ICommand ToggleRepeatCommand { get; }

    // 左栏标题文案：副标题里的「248 首 / 18.4 小时」是设计稿数字，与 AllSongs 实际的 8 首并不一致。
    // 两条都是 get-only 计算属性，读一次即定；界面在 MusicView 左栏顶部两行 TextBlock 里取用。
    public string LibraryTitle => "我的音乐库";
    public string LibrarySubtitle => "248 首歌曲 · 18.4 小时";

    // 中栏三行歌词：中间一行高亮、上下两行降透明度，纯静态文本，不随播放进度滚动。
    // 三行都是只读自动属性，XAML 里分别绑到三个 TextBlock（中间那行的字号 13.5、颜色 #DDE3F2 写在 XAML 里）。
    public string LyricsLine0 { get; } = "City lights are folding into lines";
    public string LyricsLine1 { get; } = "We built the night in quiet rooms";
    public string LyricsLine2 { get; } = "and left the door ajar for dawn";

    // TrackBar 的 Maximum 绑这里（单位：秒）；兜底 1 避免 Maximum=0 时拖动比例失真、Progress 除零。
    // 这是派生属性：依赖字段 _current，切换曲目后必须由 SelectSong 手动通知，否则 TrackBar 的刻度还停在旧曲长度。
    // 用 ?? 1 而不是 ?? 0：Maximum 等于 Minimum(0) 时，TrackBar 内部虽然把 span 兜成 1 不会算出 NaN，
    // 但拖动换算 "Value = Min + ratio * (Max - Min)" 的结果恒为 0，界面表现就是"进度条怎么拖都不动"。
    // 给一个 1 秒的假上限，至少让拖动有反馈（随后会被 Position 的 Clamp 夹回真实范围）。
    public double DurationSeconds => _current?.Seconds ?? 1;

    // 中栏歌名与艺人：Artist 额外拼了专辑名和年份，所以展示出来不是纯艺人名。
    // 两个都是派生属性，绑在中栏唱片区下方两行 TextBlock 上（20px SemiBold / 12px Text2）。
    // "—" 是"没有当前曲"时的占位符，保证界面不会出现空白行；实际流程里构造末尾就会选曲，用户看不到它。
    public string Title => _current?.Title ?? "—";
    public string Artist => _current is null ? "—" : $"{_current.Artist} · Nocturne (2025)";

    // ---------- IsPlaying 是三处状态的总开关 ----------
    // 1) 自己的 PlayIcon 在播放/暂停两个 path 之间切换；2) 当前曲的 SongItem.IsPlaying 控制列表三格跳动；
    // 3) 中栏 SpinningDisc.IsSpinning 与右栏 AudioBars.IsPlaying 直接绑它，暂停时动画一起停。
    // 先看 Set() 的返回值再往下走：值没变就不重复通知派生属性，也不重复写回当前曲。
    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            // 谁调用：TogglePlay（用户点中栏主按钮）、SelectSong（换曲时强制置 true）。
            // 前提：无 —— _current 允许为 null（构造期第一次赋值的场景），这时只更新自己。
            // 之后：绑定 IsPlaying 的三处视觉（PlayIcon 图标、SpinningDisc 转动、AudioBars 频谱）一起刷新，
            //       并把同一个值写回"当前曲"那一行的 IsPlaying（控制跳动条 / 时长文案互斥）。
            // Set 返回 false 表示"值本来就是它"，此时立刻 return：不重复通知、不重复写回当前曲。
            // 这个早返回有个被动副作用，值得记一笔：从"正在播放"状态再点同一首歌，
            // Set 返回 false → 下面那行写回不会执行，所以 SelectSong 末尾必须自己再补一次 song.IsPlaying = true。
            if (!Set(ref _isPlaying, value)) return;
            OnPropertyChanged(nameof(PlayIcon));
            if (_current is not null) _current.IsPlaying = value;
        }
    }

    // 直接返回 SVG path 字符串，XAML 里 Data="{Binding PlayIcon}" 交给 Icon 控件解析渲染；
    // 比「两个图标切 Visibility」少一层控件，也不会让两份图标同时留在视觉树上。
    // 它是派生属性：IsPlaying 变化时由 setter 手动通知；若忘了那一行，按钮图标会一直是初始值。
    // 解构取值：播放中显示 Pause（表示"点一下会暂停"），暂停中显示 Play —— 图标表达的是"下一步动作"。
    public string PlayIcon => IsPlaying ? Controls.Icons.Pause : Controls.Icons.Play;

    // 两个开关的私有 setter 只负责存值；界面颜色由下面的 *Brush 计算属性给出，切换时必须手动通知。
    // 注意界面并不直接绑 IsShuffle / IsRepeat（没有哪个控件需要这个布尔本身），
    // 所以这里的 Set 通知其实是"没人听"的，真正生效的是命令里那次对 *Brush 的手动通知。
    public bool IsShuffle
    {
        get => _isShuffle;
        private set => Set(ref _isShuffle, value);
    }

    // 循环开关默认 true；它不影响 Next 的行为（总是回到列表头），实际只改变图标颜色。
    public bool IsRepeat
    {
        get => _isRepeat;
        private set => Set(ref _isRepeat, value);
    }

    // 开关状态 -> 画刷：开启用 Accent 蓝，关闭用中性灰 #9BA3B7。
    // 这两个属性没有后备字段、不参与 Set 通知，所以构造函数里的两个命令要手动 OnPropertyChanged。
    // 实现代价：每次读取都会 new 一个 SolidColorBrush（Palette.Solid 内部就 new）。
    // 绑定只在收到通知时读一次 getter，所以这个开销可以忽略；真要抠性能，可以提成两个静态只读画刷。
    // 界面落点：中栏随机 / 循环两个图标按钮的 ctrl:Icon.Foreground。
    public IBrush ShuffleBrush => IsShuffle
        ? Palette.Solid(Palette.AccentColor)
        : Palette.Solid("#9BA3B7");

    public IBrush RepeatBrush => IsRepeat
        ? Palette.Solid(Palette.AccentColor)
        : Palette.Solid("#9BA3B7");

    // ---------- Position 的单位是「秒」，不是百分比 ----------
    // 它对应 TrackBar.Value，而 TrackBar.Maximum 绑的是 DurationSeconds，两端单位必须一致；
    // 百分比 Progress 当前没有 XAML 绑定，留给需要比例的场合；两者读同一个 _position，语义不同。
    // 必须 Clamp 到 [0, 当前曲长]：拖动可以越过 Maximum，计时器 Tick 也会把值加到超过末尾，
    // 不夹紧就会出现「4:30 / 4:12」这类超长文案，以及 Progress > 100 把进度画出轨道。
    // Set() 返回 false（值没变）时直接 return，避免每秒都重复通知两个派生属性。
    public double Position
    {
        get => _position;
        set
        {
            // 谁写这个值：① 计时器 Tick 每秒 +1；② 进度条 TrackBar（Mode=TwoWay）拖动时直接写；
            //               ③ SelectSong 换曲时置 0。
            // 前提：允许 _current 为 null，此时 max 取兜底 1，等价于"只能停在 0~1 秒"，不会崩。
            // 之后：_position 字段变了就手动通知 PositionText（左端时间文案）与 Progress（百分比，目前无人绑）。
            // max 每次现算而不是缓存：换曲后曲长就变了，缓存会让 Clamp 用旧上限。
            var max = _current?.Seconds ?? 1;
            // Math.Clamp 防两件事：拖动可以越过 Maximum 把值写大；Tick 的 +1 也会在末尾多推一格。
            // 不夹紧的后果很具体：时间文案会出现「4:13 / 4:12」，Progress 会 >100 把进度画出轨道。
            var v = Math.Clamp(value, 0, max);
            // 值没变（例如暂停时被拖动回原处、或连续两拍落在同一整数）就直接结束，避免无意义刷新。
            if (!Set(ref _position, v)) return;
            // 下面两个都是派生属性，Set 只通知了 "Position"，必须逐个补。
            OnPropertyChanged(nameof(PositionText));
            OnPropertyChanged(nameof(Progress));
        }
    }

    // 0~100 的百分比：没选曲或曲长为 0 时返回 0，顺带避免除零。
    // 如实记录现状：这个属性目前没有任何 XAML 绑定（MusicView 的进度条绑的是 Position + DurationSeconds）。
    // 它仍然被 Position setter 与 SelectSong 主动通知，属于"预留但未接线"的状态；
    // 若要接，最自然的用法是给某个宽度型容器绑 Width 或用它画第二层进度条。
    public double Progress => _current is null || _current.Seconds <= 0
        ? 0
        : _position / _current.Seconds * 100;

    // 左端时间由 Format 现算；右端 DurationText 直接取曲目自带的 "4:12" 字符串。
    // 两个都是派生属性：Position 变化只影响左边，换曲才同时影响两边，所以通知点分别落在 Position setter 和 SelectSong。
    public string PositionText => Format(_position);
    public string DurationText => _current?.Duration ?? "0:00";

    // 音量 0~100，只驱动 VolumeText 文案；没有真实音频增益可调，滑块目前只是观感。
    // 界面落点：中栏控制区右侧那个宽 64 的 TrackBar（Maximum 写死 100、TrackHeight=3，Mode=TwoWay）。
    // 谁写它：只有拖动这一个入口，没有任何代码在别处改音量。
    public double Volume
    {
        get => _volume;
        set
        {
            if (Set(ref _volume, value)) OnPropertyChanged(nameof(VolumeText));
        }
    }

    public string VolumeText => $"{Math.Round(_volume)}%";
    // VolumeText 现状：没有任何 XAML 绑定（音量组里只有一个喇叭图标 + 滑块，没有百分比文字）。
    // 保留它 + Volume setter 里那次通知，是为将来在滑块右侧补一个数字标签时可以直接用。
    // Math.Round 把 0~100 的小数收成整数，避免拖出 "62.34567%" 这种文案。

    // 秒 -> m:ss（分钟不补零、秒补两位），负数按 0 处理，避免拖动越界时出现 -1:-5 这种文案。
    // 为什么负数要单独兜：TrackBar 在极端窄的宽度下算出的比例可能略小于 0，Clamp 已经拦了一层，
    // 但 Format 是 public 之外唯一的时间出口，多加一道 Max(0) 让这个函数单独看也是安全的。
    // 分钟不补零是刻意选择：4:12 比 04:12 更像音乐播放器的写法。
    private static string Format(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:00}";
    }

    // ---------- 切换当前曲：先全量归零，再点亮目标曲 ----------
    // 为什么先把所有歌的 IsPlaying 置 false：它是列表里三格跳动指示器的开关，
    // 不清零就会出现「上一首还在跳、新歌也在跳」的双高亮。
    // IsSelected 同理是单选语义（IsCurrent 就是它的别名），不清理的话旧行会一直保留选中底色。
    // 顺序也有讲究：归零 -> 换 _current -> Position=0 -> 最后 IsPlaying=true，
    // 这样计时器下一次 Tick 读到的一定是新曲的 Position/Seconds，不会拿旧进度去比新时长。
    // 末尾集中通知 Title / Artist / DurationText / DurationSeconds / Progress / PositionText：
    // 它们都依赖普通字段 _current，不会自动触发通知，必须逐个补。
    private void SelectSong(SongItem song)
    {
        // 谁调用：构造函数的步骤 6（默认第一首）、SelectSongCommand（点列表行）、Next / Prev（换曲）。
        // 前提：song 必须是 AllSongs 里的元素，否则 IndexOf 找不到（Next/Prev 会在下标 -1 上取模出错）。
        // 之后：_current 指向新曲、Position 归零、IsPlaying 为 true、只有这一首 IsPlaying/IsSelected，
        //       中栏的 Title / Artist / 时长与进度条全部刷新。
        //
        // 步骤 1：全量重置。遍历的是 AllSongs 而不是 VisibleSongs —— 被筛掉的行虽然不在屏幕上，
        //         但仍是同一批对象，不清理的话切回「全部」时会看到两行同时高亮。
        //         ReferenceEquals 而非 ==：SongItem 没有重载运算符，两者等价，这里写 ReferenceEquals
        //         是为了把"必须是同一个实例"这层意思写明（值相同也不认）。
        foreach (var s in AllSongs)
        {
            s.IsSelected = ReferenceEquals(s, song);
            s.IsPlaying = false;
        }

        // 步骤 2：换 _current。这一行必须早于下面所有赋值与通知 —— 所有派生属性都从它取值。
        //         若放到通知之后，通知到达时 getter 读到的还是旧曲，界面会白刷一次且不会自动纠正。
        //         若放到 IsPlaying = true 之后更糟：setter 里"写回当前曲 IsPlaying"那行会把旧曲重新点亮，
        //         结果列表里两行同时在跳（旧行 + 新行）。
        _current = song;
        // 步骤 3：给新歌置位。IsSelected 在步骤 1 就用 ReferenceEquals 设过了，这里再写一次是无害的显式声明；
        //         Set 返回 false（值没变）时什么也不做。
        song.IsSelected = true;
        // 步骤 4：进度归零。此刻 _current 已是新曲，所以 Clamp 的上限取的是新曲长度 ——
        //         这解决了"从 6:18 换到 3:12，进度却还停在 5 分钟"的越界问题。
        //         若旧进度本来就是 0，Set 返回 false 不通知，末尾那次 PositionText / Progress 通知会兜住。
        Position = 0;
        // 步骤 5：置为播放态。若本来就是 true（正在听着换歌），Set 早返回，不会执行"写回当前曲"。
        //         而步骤 1 刚把新曲的 IsPlaying 清成 false，所以下面这行必须补上。
        IsPlaying = true;
        song.IsPlaying = true;
        // 步骤 6：一次性通知 6 个派生属性。它们都依赖字段 _current，Set 的通知覆盖不到。
        //         集中放在最后而不是分散到各处，是为了避免中间态被界面看到：
        //         比如"新歌名 + 旧时长"的一帧会让进度条按旧 Maximum 摆一次位置，观感是抖一下。
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Artist));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(PositionText));
    }

    // 播放/暂停：只翻转 IsPlaying，图标、唱片、频谱的联动都在它的 setter 里。
    // 谁调用：中栏 46px 主按钮（TogglePlayCommand）。前提：无。
    // 之后：Tick 的早返回条件随之改变 —— 暂停后计时器仍在跑，但每秒都被 !IsPlaying 挡回去，进度原地不动。
    private void TogglePlay()
    {
        IsPlaying = !IsPlaying;
    }

    // 下一首：随机模式取 0..Count-1 的随机下标，若正好等于当前下标就顺延一位（+1 取模）。
    // 排除当前曲是为了避免「点了下一首还在唱同一首」—— 随机允许重复，但至少要换一首。
    // 顺序模式按 (index+1) % Count 天然循环；_current 为空时兜底选第一首，保证不会空转。
    private void Next()
    {
        // 谁调用：NextCommand（中栏右箭头）、以及 Tick 判定播完之后自动调一次。
        // 前提：无（_current 为 null 时有兜底）。之后：走 SelectSong，等价于"用户点了另一首歌"。
        // 何时触发：列表末尾播完 → 回到第一首；这也是 IsRepeat 唯一"看起来像"的地方（其实它只改颜色）。
        if (_current is null)
        {
            // 兜底：还没选过曲时不能 IndexOf(null)，直接选第一首，保证不会空转。
            SelectSong(AllSongs[0]);
            return;
        }

        // IndexOf 按引用比较（SongItem 未重载 Equals），所以找不到时返回 -1 ——
        // 后续 (index + 1) % Count 会把 -1 变成 0，意外地跳到第一首，而不是崩掉。
        var index = AllSongs.IndexOf(_current);
        if (IsShuffle)
        {
            // 每次 new Random()：.NET 6 起无参构造用的是线程级种子，连续 new 不会拿到同一串序列，
            // 所以这里没有"同一秒内多次 new 得到同样随机数"的老坑（.NET Framework 时代才需要共享实例）。
            var rnd = new Random();
            // Next(Count) 取 [0, Count) 的左闭右开区间，正好覆盖全部下标。
            var next = rnd.Next(AllSongs.Count);
            // 随机允许重复，但"点下一首还是这一首"体验上等于没反应，所以撞车就顺延一位。
            // 顺延用 (next + 1) % Count 而不是 next + 1：撞在最后一首时不会越界。
            // 退化情况：Count == 1 时 %1 恒为 0，顺延也躲不开同一首 —— 这是无法避免的数学事实，不是 bug。
            if (next == index) next = (next + 1) % AllSongs.Count;
            SelectSong(AllSongs[next]);
            return;
        }

        // 顺序模式：环形取模。末尾 (Count-1)+1 取模回 0，开头永远不会出现负下标。
        SelectSong(AllSongs[(index + 1) % AllSongs.Count]);
    }

    // 上一首：顺序回退，(index-1+Count) % Count 处理第一首的负数下标，不走随机分支。
    private void Prev()
    {
        // 谁调用：PrevCommand（中栏左箭头）。注意随机模式下 Prev 依然按列表回退，
        // 因为"上一首"需要历史栈才有意义，而本 VM 没有播放历史，只能退化成顺序回退。
        if (_current is null)
        {
            // 同 Next：没选过曲就无法 IndexOf，兜底第一首。
            SelectSong(AllSongs[0]);
            return;
        }

        var index = AllSongs.IndexOf(_current);
        // 环形取模的关键在 + Count：第一首的 index 是 0，0 - 1 = -1，直接 % Count 在 C# 里仍是 -1
        // （C# 的 % 会保留被除数符号），会拿 AllSongs[-1] 抛异常；先 + Count 把它抬回 Count-1。
        SelectSong(AllSongs[(index - 1 + AllSongs.Count) % AllSongs.Count]);
    }

    // 页签筛选：直接拿中文字符串当匹配键（全部 / 收藏 / 最近 / 专辑），
    // 所以 Tabs 里的 Label 文案与这里的 switch 分支必须保持一致，改一处就要改另一处。
    // 做法是先清空 VisibleSongs 再按条件重新填充，ObservableCollection 会逐条通知界面刷新。
    // 「专辑」没有真实专辑字段，用 Take(4) 假装前四首同属一张专辑；筛选不影响当前播放与右侧队列。
    private void ApplyFilter(string filter)
    {
        // 谁调用：FilterCommand（左栏四个页签）。前提：无，VisibleSongs 一定已初始化。
        // 参数来源：只有 Tabs 的点击会走到这里，而这个参数当前恒为 "全部"（原因见构造函数步骤 2 的链条说明）。
        // 之后：VisibleSongs 内容被替换成 AllSongs 的一个投影；_current、播放状态、右侧队列都不受影响。
        //
        // 步骤 1：清空。必须原地 Clear 而不是 VisibleSongs = new ...，
        //         因为 XAML 的 ItemsSource 绑的是这个属性、而且属性没有 setter，换实例根本绑不上。
        //         Clear 会发一次 Reset 通知，界面据此把整个列表的行控件全部丢弃。
        VisibleSongs.Clear();
        // 步骤 2：按中文字符串选投影。用 string switch 而不是 enum，好处是构造 Tabs 时能直接写文案；
        //         代价是文案与分支必须人工保持一致 —— 改「收藏」为「喜欢」而忘了这里，就会静默落到兜底分支。
        //         兜底 _ => AllSongs：任何不认识的键都当"全部"，所以拼错不会报错、只会"筛选失灵"。
        //         "专辑"没有真实专辑字段，用 Take(4) 假装前四首同属一张专辑。
        //         Where / Take 都是惰性求值，真正的物化发生在下面的 foreach。
        IEnumerable<SongItem> source = filter switch
        {
            "收藏" => AllSongs.Where(s => s.IsFavorite),
            "最近" => AllSongs.Where(s => s.IsRecent),
            "专辑" => AllSongs.Take(4),
            _ => AllSongs
        };
        // 步骤 3：逐条加回。每条 Add 各发一次 CollectionChanged，界面按条目增量插入行；
        //         这些 SongItem 与 AllSongs 里的是同一批实例，所以选中/播放状态天然保持。
        //         顺带一个事实：筛掉当前播放曲时，计时器仍在推进那首，只是列表里看不到它。
        foreach (var song in source) VisibleSongs.Add(song);
    }
}
