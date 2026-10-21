using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Aster.ViewModels;

public sealed class SongItem : SelectableItem
{
    public string Title { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public string Duration { get; init; } = string.Empty;
    public double Seconds { get; init; }
    public IBrush Cover { get; init; } = Palette.Solid("#2B2F55");
    public bool IsFavorite { get; init; }
    public bool IsRecent { get; init; }
    public bool IsLocal { get; init; }
    public ICommand Command { get; init; } = null!;

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set => Set(ref _isPlaying, value);
    }

    public bool IsCurrent => IsSelected;
}

public sealed class MusicViewModel : ObservableObject
{
    private readonly DispatcherTimer _timer;
    private SongItem? _current;
    private bool _isPlaying = true;
    private bool _isShuffle;
    private bool _isRepeat = true;
    private double _position;
    private double _volume = 62;

    public MusicViewModel()
    {
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

        FilterCommand = new RelayCommand(o => ApplyFilter(o as string ?? "全部"));
        SelectSongCommand = new RelayCommand(o =>
        {
            if (o is SongItem song) SelectSong(song);
        });
        TogglePlayCommand = new RelayCommand(TogglePlay);
        NextCommand = new RelayCommand(Next);
        PrevCommand = new RelayCommand(Prev);
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

        VisibleSongs = new ObservableCollection<SongItem>(AllSongs);
        Queue = new ObservableCollection<PlainRow>(AllSongs.Skip(1).Take(6)
            .Select((s, i) => new PlainRow { Index = (i + 1).ToString(), Text = s.Title }));

        Tabs = new ObservableCollection<SegmentItem>
        {
            new("全部", FilterCommand),
            new("收藏", FilterCommand),
            new("最近", FilterCommand),
            new("专辑", FilterCommand)
        };
        Tabs[0].IsSelected = true;

        SelectSong(AllSongs[0]);
    }

    public List<SongItem> AllSongs { get; }
    public ObservableCollection<SongItem> VisibleSongs { get; }
    public ObservableCollection<PlainRow> Queue { get; }
    public ObservableCollection<SegmentItem> Tabs { get; }

    public ICommand FilterCommand { get; }
    public ICommand SelectSongCommand { get; }
    public ICommand TogglePlayCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PrevCommand { get; }
    public ICommand ToggleShuffleCommand { get; }
    public ICommand ToggleRepeatCommand { get; }

    public string LibraryTitle => "我的音乐库";
    public string LibrarySubtitle => "248 首歌曲 · 18.4 小时";

    public string LyricsLine0 { get; } = "City lights are folding into lines";
    public string LyricsLine1 { get; } = "We built the night in quiet rooms";
    public string LyricsLine2 { get; } = "and left the door ajar for dawn";

    public double DurationSeconds => _current?.Seconds ?? 1;

    public string Title => _current?.Title ?? "—";
    public string Artist => _current is null ? "—" : $"{_current.Artist} · Nocturne (2025)";

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

    public string PlayIcon => IsPlaying ? Controls.Icons.Pause : Controls.Icons.Play;

    public bool IsShuffle
    {
        get => _isShuffle;
        private set => Set(ref _isShuffle, value);
    }

    public bool IsRepeat
    {
        get => _isRepeat;
        private set => Set(ref _isRepeat, value);
    }

    public IBrush ShuffleBrush => IsShuffle
        ? Palette.Solid(Palette.AccentColor)
        : Palette.Solid("#9BA3B7");

    public IBrush RepeatBrush => IsRepeat
        ? Palette.Solid(Palette.AccentColor)
        : Palette.Solid("#9BA3B7");

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

    public double Progress => _current is null || _current.Seconds <= 0
        ? 0
        : _position / _current.Seconds * 100;

    public string PositionText => Format(_position);
    public string DurationText => _current?.Duration ?? "0:00";

    public double Volume
    {
        get => _volume;
        set
        {
            if (Set(ref _volume, value)) OnPropertyChanged(nameof(VolumeText));
        }
    }

    public string VolumeText => $"{Math.Round(_volume)}%";

    private static string Format(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:00}";
    }

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

    private void TogglePlay()
    {
        IsPlaying = !IsPlaying;
    }

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
