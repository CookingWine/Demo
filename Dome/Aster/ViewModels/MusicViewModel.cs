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
    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set => Set(ref _isPlaying, value);
    }

    // 选中态与「当前曲」是同一件事，这里给 XAML 一个语义更清楚的别名（IsSelected 来自 SelectableItem）。
    public bool IsCurrent => IsSelected;
}

/// <summary>
/// 音乐播放器视图模型：假播放实现（DispatcherTimer 每秒推进一秒），不依赖任何音频库。
/// 左栏曲库与筛选、中栏唱片/歌词/控制、右栏可视化与队列的状态都集中在这里。
/// </summary>
public sealed class MusicViewModel : ObservableObject
{
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

    public MusicViewModel()
    {
        // ---------- 「假播放」的核心：DispatcherTimer 每秒 Tick 一次 ----------
        // 链路：IsPlaying 且存在当前曲 -> Position + 1 秒 -> 到达该曲 Seconds 后归零并自动 Next()。
        // Position 的 setter 会连带通知 PositionText / Progress，所以时间文案与进度条自动跟着走。
        // 坑：没有音频解码、也没有播放位置校准 —— 拖动进度条只是改数字，声音不会真的跳转。
        // 坑：timer 在构造时就 Start 且从不 Stop，视图销毁后仍在跑（因未挂导航，暂未暴露问题）。
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            if (!IsPlaying || _current is null) return;
            Position += 1;
            if (Position >= _current.Seconds)
            {
                Position = 0;
                Next();
            }
        };
        _timer.Start();

        // ---------- 命令：全是同步 RelayCommand，没有 CanExecute 约束 ----------
        // 页签与歌曲项共用 FilterCommand / SelectSongCommand，靠 CommandParameter 区分目标。
        // 坑：XAML 里 CommandParameter 传的是 SegmentItem 对象，而这里用 o as string 取值，
        //     所以「收藏 / 最近 / 专辑」点下去实际都落到兜底分支「全部」，筛选并未真正生效。
        FilterCommand = new RelayCommand(o => ApplyFilter(o as string ?? "全部"));
        // 歌曲项点击：参数就是被点的 SongItem（XAML 里 CommandParameter="{Binding}"）。
        SelectSongCommand = new RelayCommand(o =>
        {
            if (o is SongItem song) SelectSong(song);
        });
        TogglePlayCommand = new RelayCommand(TogglePlay);
        NextCommand = new RelayCommand(Next);
        PrevCommand = new RelayCommand(Prev);
        // 随机 / 循环开关只翻转布尔值，再手动 OnPropertyChanged(ShuffleBrush / RepeatBrush)：
        // 画刷是没有后备字段的计算属性，Set() 的通知覆盖不到，必须显式补一次，否则颜色不刷新。
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
        VisibleSongs = new ObservableCollection<SongItem>(AllSongs);
        // 队列固定取第 2~7 首做「接下来 6 首」的静态展示，与真实播放顺序（含随机）没有联动。
        Queue = new ObservableCollection<PlainRow>(AllSongs.Skip(1).Take(6)
            .Select((s, i) => new PlainRow { Index = (i + 1).ToString(), Text = s.Title }));

        // 页签复用同一个 FilterCommand，Label 既是显示文案也是筛选键，改文案要同步改 ApplyFilter 的 switch。
        Tabs = new ObservableCollection<SegmentItem>
        {
            new("全部", FilterCommand),
            new("收藏", FilterCommand),
            new("最近", FilterCommand),
            new("专辑", FilterCommand)
        };
        // 默认选中「全部」，与末尾的 SelectSong(AllSongs[0]) 一起构成进入页面时的初始状态。
        Tabs[0].IsSelected = true;

        SelectSong(AllSongs[0]);
    }

    // ---------- 对外暴露的集合（XAML 只读绑定） ----------
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
    public string LibraryTitle => "我的音乐库";
    public string LibrarySubtitle => "248 首歌曲 · 18.4 小时";

    // 中栏三行歌词：中间一行高亮、上下两行降透明度，纯静态文本，不随播放进度滚动。
    public string LyricsLine0 { get; } = "City lights are folding into lines";
    public string LyricsLine1 { get; } = "We built the night in quiet rooms";
    public string LyricsLine2 { get; } = "and left the door ajar for dawn";

    // TrackBar 的 Maximum 绑这里（单位：秒）；兜底 1 避免 Maximum=0 时拖动比例失真、Progress 除零。
    public double DurationSeconds => _current?.Seconds ?? 1;

    // 中栏歌名与艺人：Artist 额外拼了专辑名和年份，所以展示出来不是纯艺人名。
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
            if (!Set(ref _isPlaying, value)) return;
            OnPropertyChanged(nameof(PlayIcon));
            if (_current is not null) _current.IsPlaying = value;
        }
    }

    // 直接返回 SVG path 字符串，XAML 里 Data="{Binding PlayIcon}" 交给 Icon 控件解析渲染；
    // 比「两个图标切 Visibility」少一层控件，也不会让两份图标同时留在视觉树上。
    public string PlayIcon => IsPlaying ? Controls.Icons.Pause : Controls.Icons.Play;

    // 两个开关的私有 setter 只负责存值；界面颜色由下面的 *Brush 计算属性给出，切换时必须手动通知。
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
            var max = _current?.Seconds ?? 1;
            var v = Math.Clamp(value, 0, max);
            if (!Set(ref _position, v)) return;
            OnPropertyChanged(nameof(PositionText));
            OnPropertyChanged(nameof(Progress));
        }
    }

    // 0~100 的百分比：没选曲或曲长为 0 时返回 0，顺带避免除零。
    public double Progress => _current is null || _current.Seconds <= 0
        ? 0
        : _position / _current.Seconds * 100;

    // 左端时间由 Format 现算；右端 DurationText 直接取曲目自带的 "4:12" 字符串。
    public string PositionText => Format(_position);
    public string DurationText => _current?.Duration ?? "0:00";

    // 音量 0~100，只驱动 VolumeText 文案；没有真实音频增益可调，滑块目前只是观感。
    public double Volume
    {
        get => _volume;
        set
        {
            if (Set(ref _volume, value)) OnPropertyChanged(nameof(VolumeText));
        }
    }

    public string VolumeText => $"{Math.Round(_volume)}%";

    // 秒 -> m:ss（分钟不补零、秒补两位），负数按 0 处理，避免拖动越界时出现 -1:-5 这种文案。
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
        foreach (var s in AllSongs)
        {
            s.IsSelected = ReferenceEquals(s, song);
            s.IsPlaying = false;
        }

        _current = song;
        song.IsSelected = true;
        Position = 0;
        IsPlaying = true;
        song.IsPlaying = true;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Artist));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(PositionText));
    }

    // 播放/暂停：只翻转 IsPlaying，图标、唱片、频谱的联动都在它的 setter 里。
    private void TogglePlay()
    {
        IsPlaying = !IsPlaying;
    }

    // 下一首：随机模式取 0..Count-1 的随机下标，若正好等于当前下标就顺延一位（+1 取模）。
    // 排除当前曲是为了避免「点了下一首还在唱同一首」—— 随机允许重复，但至少要换一首。
    // 顺序模式按 (index+1) % Count 天然循环；_current 为空时兜底选第一首，保证不会空转。
    private void Next()
    {
        if (_current is null)
        {
            SelectSong(AllSongs[0]);
            return;
        }

        var index = AllSongs.IndexOf(_current);
        if (IsShuffle)
        {
            var rnd = new Random();
            var next = rnd.Next(AllSongs.Count);
            if (next == index) next = (next + 1) % AllSongs.Count;
            SelectSong(AllSongs[next]);
            return;
        }

        SelectSong(AllSongs[(index + 1) % AllSongs.Count]);
    }

    // 上一首：顺序回退，(index-1+Count) % Count 处理第一首的负数下标，不走随机分支。
    private void Prev()
    {
        if (_current is null)
        {
            SelectSong(AllSongs[0]);
            return;
        }

        var index = AllSongs.IndexOf(_current);
        SelectSong(AllSongs[(index - 1 + AllSongs.Count) % AllSongs.Count]);
    }

    // 页签筛选：直接拿中文字符串当匹配键（全部 / 收藏 / 最近 / 专辑），
    // 所以 Tabs 里的 Label 文案与这里的 switch 分支必须保持一致，改一处就要改另一处。
    // 做法是先清空 VisibleSongs 再按条件重新填充，ObservableCollection 会逐条通知界面刷新。
    // 「专辑」没有真实专辑字段，用 Take(4) 假装前四首同属一张专辑；筛选不影响当前播放与右侧队列。
    private void ApplyFilter(string filter)
    {
        VisibleSongs.Clear();
        IEnumerable<SongItem> source = filter switch
        {
            "收藏" => AllSongs.Where(s => s.IsFavorite),
            "最近" => AllSongs.Where(s => s.IsRecent),
            "专辑" => AllSongs.Take(4),
            _ => AllSongs
        };
        foreach (var song in source) VisibleSongs.Add(song);
    }
}
