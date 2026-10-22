// ============================================================================
// Items.cs —— 纯展示模型 + 设计稿色板（只做数据载体，不含业务流程）
// 1) Palette：把设计稿的十六进制色值 / Color 转成 Avalonia 画刷（Solid/Alpha/Gradient）；
// 2) 导航模型：RailItem（74px 一级 rail）与 SubNavEntry 家族（二级导航的分组与链接）；
// 3) 内容模型：KpiItem、RecentDocItem、QuickAction、PlainRow、SegmentItem。
// 位置：MainWindowViewModel 负责构造这些集合，XAML 通过 {Binding} 直接消费。
// 坑：颜色统一走 Palette，不要在 XAML 里再硬编码；带 Kind/Tone 的模型改配色时只改映射表。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;

namespace Aster.ViewModels;

// ---------- 色板 ----------
/// <summary>设计稿色板，供视图模型直接构造画刷。</summary>
public static class Palette
{
    // 语义色常量：品牌蓝 / 紫 / 青 / 绿（正向）/ 琥珀（预警）/ 红（负向）/ 深色底。
    public static readonly Color AccentColor = Color.Parse("#6C8BFF");
    public static readonly Color VioletColor = Color.Parse("#A78BFA");
    public static readonly Color CyanColor = Color.Parse("#3DD6E8");
    public static readonly Color GreenColor = Color.Parse("#3DDC97");
    public static readonly Color AmberColor = Color.Parse("#F5B942");
    public static readonly Color RedColor = Color.Parse("#F8716E");
    public static readonly Color Bg2Color = Color.Parse("#13161D");

    /// <summary>十六进制字符串 → 纯色画刷，写 XAML 风格的色值最省事。</summary>
    public static IBrush Solid(string hex) => new SolidColorBrush(Color.Parse(hex));

    /// <summary>Color → 纯色画刷，用于复用上面的语义色常量。</summary>
    public static IBrush Solid(Color c) => new SolidColorBrush(c);

    /// <summary>
    /// 保留原色、只改透明度的画刷：alpha 传 0~1 的倍数（如 0.10），内部换算成 0~255。
    /// 主要用于「浅色底 + 同色文字」的标签，比如 KpiItem.DeltaBackground。
    /// </summary>
    public static IBrush Alpha(Color c, double alpha)
        => new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), c.R, c.G, c.B));

    /// <summary>Alpha 的字符串重载，便于直接写设计稿色值。</summary>
    public static IBrush Alpha(string hex, double alpha) => Alpha(Color.Parse(hex), alpha);

    /// <summary>
    /// 两色线性渐变。horizontal=true 时从左到右，否则自上而下；
    /// RelativePoint 用相对单位，保证画刷跟随控件尺寸拉伸。
    /// </summary>
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

// ---------- 导航模型 ----------
/// <summary>可选中列表项的通用外壳。</summary>
public abstract class SelectableItem : ObservableObject
{
    private bool _isSelected;

    /// <summary>
    /// 选中态。放在基类而不是各自再写一遍，是为了让 DataTemplate 统一用 IsSelected 驱动高亮样式，
    /// 由一个 VM 集中维护「同一组里只有一项为 true」（见 MainWindowViewModel.SelectModule）。
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

/// <summary>一级导航（74px rail）。</summary>
public sealed class RailItem : SelectableItem
{
    /// <summary>
    /// 构造一级导航项。Key 是后续 switch 分支的判别依据；
    /// IsSettings 在构造时按 key 算好，避免在 XAML 里做字符串比较。
    /// </summary>
    public RailItem(string key, string title, string icon, ICommand command)
    {
        Key = key;
        Title = title;
        Icon = icon;
        Command = command;
        IsSettings = key == "settings";
    }

    // 全部只读：导航项在运行期不变，只有 IsSelected 会变（所以继承 SelectableItem）。
    public string Key { get; }
    public string Title { get; }
    public string Icon { get; }
    public ICommand Command { get; }
    /// <summary>「设置」不是切换模块，而是打开右侧浮层，模板据此把它单独摆到 rail 底部。</summary>
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

/// <summary>二级导航里的分组标题（如「项目」「最近」），只负责视觉分节，不可点击。</summary>
public sealed class SubNavGroupEntry : SubNavEntry
{
    public SubNavGroupEntry(string title) => Title = title;
    public string Title { get; }
}

/// <summary>
/// 二级导航里的可点击链接。参数较多，是因为同一个模板要兼顾三种形态：
/// 常规入口（带图标 + 右侧数字徽标）、强调入口（Emphasized，字号/字重更大）、
/// 「最近」列表（ShowIcon=false、Height 更矮）。
/// </summary>
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
    /// <summary>给徽标标签的 IsVisible 用：Badge 为空时不占位，避免空白留白。</summary>
    public bool HasBadge => !string.IsNullOrEmpty(Badge);
    public bool ShowIcon { get; }
    public bool Emphasized { get; }
    public double Height { get; }
    // 字号与字重由 Emphasized 派生，保证模板里不用再写值转换器。
    public double TitleFontSize => Emphasized ? 15 : 14;
    public Avalonia.Media.FontWeight TitleWeight => Emphasized ? Avalonia.Media.FontWeight.Medium : Avalonia.Media.FontWeight.Normal;
}

// ---------- 内容模型 ----------
/// <summary>指标卡。</summary>
public sealed class KpiItem
{
    // 全部用 init：这些卡片在构造时一次性铺好数据，之后不再变，因此不需要 INotifyPropertyChanged。
    public string Title { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string? Unit { get; init; }
    public string? Tail { get; init; }
    public string Delta { get; init; } = string.Empty;
    /// <summary>涨跌语义，只接受 up / dn / warn / 其它（中性），决定下面两个画刷的颜色。</summary>
    public string DeltaKind { get; init; } = "up";
    public string? Icon { get; init; }
    public IReadOnlyList<double>? Spark { get; init; }
    public IBrush? SparkBrush { get; init; }

    // 空字符串统一视为「没有」，方便 XAML 直接绑 IsVisible，无需再写转换器。
    public bool HasIcon => !string.IsNullOrEmpty(Icon);
    /// <summary>折线至少要两个点才成立，少于两点直接不显示迷你图。</summary>
    public bool HasSpark => Spark is { Count: > 1 };
    public IBrush? ValueBrushOverride { get; init; }

    /// <summary>数值默认浅白；个别卡片（如告警）可传 ValueBrushOverride 改色。</summary>
    public IBrush ValueBrush => ValueBrushOverride ?? Palette.Solid("#E9ECF4");

    /// <summary>
    /// DeltaKind → 涨跌文字色：up 绿、dn 红、warn 琥珀，其余走中性灰。
    /// 之所以用字符串常量而不是枚举，是为了在构造 KpiItem 时可以直接写字面量。
    /// </summary>
    public IBrush DeltaBrush => DeltaKind switch
    {
        "up" => Palette.Solid(Palette.GreenColor),
        "dn" => Palette.Solid(Palette.RedColor),
        "warn" => Palette.Solid(Palette.AmberColor),
        _ => Palette.Solid("#9BA3B7")
    };

    /// <summary>与 DeltaBrush 同源的标签底色，统一取 10% 透明度，保证「浅底 + 同色字」的观感。</summary>
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

/// <summary>最近文档行。Tone 决定图标底色与图标前景：violet / cyan / 默认 accent。</summary>
public sealed class RecentDocItem
{
    public string Name { get; init; } = string.Empty;
    public string Meta { get; init; } = string.Empty;
    public string Time { get; init; } = string.Empty;
    public string Tone { get; init; } = "accent";

    /// <summary>图标底色是同色 12% 透明，避免整行过亮。</summary>
    public IBrush IconBackground => Tone switch
    {
        "violet" => Palette.Alpha(Palette.VioletColor, 0.12),
        "cyan" => Palette.Alpha(Palette.CyanColor, 0.12),
        _ => Palette.Alpha(Palette.AccentColor, 0.12)
    };

    /// <summary>图标前景用实色，与上面的淡底形成对比。</summary>
    public IBrush IconBrush => Tone switch
    {
        "violet" => Palette.Solid(Palette.VioletColor),
        "cyan" => Palette.Solid(Palette.CyanColor),
        _ => Palette.Solid(Palette.AccentColor)
    };
}

/// <summary>快捷入口（图标 + 文案）。点击行为由外层模板决定，当前 HomeView 只渲染按钮、未绑命令，所以自身不带 Command。</summary>
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
    // 继承 SelectableItem：靠 IsSelected 让模板把当前段渲染成高亮样式。
    public SegmentItem(string label, ICommand command)
    {
        Label = label;
        Command = command;
    }

    public string Label { get; }
    public ICommand Command { get; }
}
