using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;

namespace Aster.ViewModels;

/// <summary>设计稿色板，供视图模型直接构造画刷。</summary>
public static class Palette
{
    public static readonly Color AccentColor = Color.Parse("#6C8BFF");
    public static readonly Color VioletColor = Color.Parse("#A78BFA");
    public static readonly Color CyanColor = Color.Parse("#3DD6E8");
    public static readonly Color GreenColor = Color.Parse("#3DDC97");
    public static readonly Color AmberColor = Color.Parse("#F5B942");
    public static readonly Color RedColor = Color.Parse("#F8716E");
    public static readonly Color Bg2Color = Color.Parse("#13161D");

    public static IBrush Solid(string hex) => new SolidColorBrush(Color.Parse(hex));

    public static IBrush Solid(Color c) => new SolidColorBrush(c);

    public static IBrush Alpha(Color c, double alpha)
        => new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), c.R, c.G, c.B));

    public static IBrush Alpha(string hex, double alpha) => Alpha(Color.Parse(hex), alpha);

    public static IBrush Gradient(string from, string to, bool horizontal = false)
        => new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = horizontal
                ? new Avalonia.RelativePoint(1, 0, Avalonia.RelativeUnit.Relative)
                : new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse(from), 0),
                new GradientStop(Color.Parse(to), 1)
            }
        };
}

/// <summary>可选中列表项的通用外壳。</summary>
public abstract class SelectableItem : ObservableObject
{
    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

/// <summary>一级导航（74px rail）。</summary>
public sealed class RailItem : SelectableItem
{
    public RailItem(string key, string title, string icon, ICommand command)
    {
        Key = key;
        Title = title;
        Icon = icon;
        Command = command;
        IsSettings = key == "settings";
    }

    public string Key { get; }
    public string Title { get; }
    public string Icon { get; }
    public ICommand Command { get; }
    public bool IsSettings { get; }
}

/// <summary>二级导航条目基类。</summary>
public abstract class SubNavEntry : ObservableObject
{
    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

public sealed class SubNavGroupEntry : SubNavEntry
{
    public SubNavGroupEntry(string title) => Title = title;
    public string Title { get; }
}

public sealed class SubNavLinkEntry : SubNavEntry
{
    public SubNavLinkEntry(string key, string title, string icon, ICommand command, string? badge = null,
        bool showIcon = true, bool emphasized = false, double height = 38)
    {
        Key = key;
        Title = title;
        Icon = icon;
        Command = command;
        Badge = badge;
        ShowIcon = showIcon;
        Emphasized = emphasized;
        Height = height;
    }

    public string Key { get; }
    public string Title { get; }
    public string Icon { get; }
    public ICommand Command { get; }
    public string? Badge { get; }
    public bool HasBadge => !string.IsNullOrEmpty(Badge);
    public bool ShowIcon { get; }
    public bool Emphasized { get; }
    public double Height { get; }
    public double TitleFontSize => Emphasized ? 15 : 14;
    public Avalonia.Media.FontWeight TitleWeight => Emphasized ? Avalonia.Media.FontWeight.Medium : Avalonia.Media.FontWeight.Normal;
}

/// <summary>指标卡。</summary>
public sealed class KpiItem
{
    public string Title { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string? Unit { get; init; }
    public string? Tail { get; init; }
    public string Delta { get; init; } = string.Empty;
    public string DeltaKind { get; init; } = "up";
    public string? Icon { get; init; }
    public IReadOnlyList<double>? Spark { get; init; }
    public IBrush? SparkBrush { get; init; }

    public bool HasIcon => !string.IsNullOrEmpty(Icon);
    public bool HasSpark => Spark is { Count: > 1 };
    public IBrush? ValueBrushOverride { get; init; }

    public IBrush ValueBrush => ValueBrushOverride ?? Palette.Solid("#E9ECF4");

    public IBrush DeltaBrush => DeltaKind switch
    {
        "up" => Palette.Solid(Palette.GreenColor),
        "dn" => Palette.Solid(Palette.RedColor),
        "warn" => Palette.Solid(Palette.AmberColor),
        _ => Palette.Solid("#9BA3B7")
    };

    public IBrush DeltaBackground => DeltaKind switch
    {
        "up" => Palette.Alpha(Palette.GreenColor, 0.10),
        "dn" => Palette.Alpha(Palette.RedColor, 0.10),
        "warn" => Palette.Alpha(Palette.AmberColor, 0.10),
        _ => Palette.Alpha("#FFFFFF", 0.05)
    };

    public bool HasTail => !string.IsNullOrEmpty(Tail);
    public bool HasUnit => !string.IsNullOrEmpty(Unit);
}

public sealed class RecentDocItem
{
    public string Name { get; init; } = string.Empty;
    public string Meta { get; init; } = string.Empty;
    public string Time { get; init; } = string.Empty;
    public string Tone { get; init; } = "accent";

    public IBrush IconBackground => Tone switch
    {
        "violet" => Palette.Alpha(Palette.VioletColor, 0.12),
        "cyan" => Palette.Alpha(Palette.CyanColor, 0.12),
        _ => Palette.Alpha(Palette.AccentColor, 0.12)
    };

    public IBrush IconBrush => Tone switch
    {
        "violet" => Palette.Solid(Palette.VioletColor),
        "cyan" => Palette.Solid(Palette.CyanColor),
        _ => Palette.Solid(Palette.AccentColor)
    };
}

public sealed class QuickAction
{
    public string Title { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
}

/// <summary>简单展示行（队列、导出记录等）。</summary>
public sealed class PlainRow
{
    public string Index { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public string? Value { get; init; }
}

/// <summary>分段控件项。</summary>
public sealed class SegmentItem : SelectableItem
{
    public SegmentItem(string label, ICommand command)
    {
        Label = label;
        Command = command;
    }

    public string Label { get; }
    public ICommand Command { get; }
}
