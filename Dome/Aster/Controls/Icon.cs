using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Aster.Controls;

/// <summary>
/// 极简矢量图标：以 24x24 的 SVG path 数据绘制，颜色跟随继承的 Foreground。
/// </summary>
public class Icon : Control
{
    private Geometry? _geometry;

    public static readonly StyledProperty<string?> DataProperty =
        AvaloniaProperty.Register<Icon, string?>(nameof(Data));

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(Thickness), 1.6);

    public static readonly StyledProperty<bool> FilledProperty =
        AvaloniaProperty.Register<Icon, bool>(nameof(Filled));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Icon>();

    static Icon()
    {
        AffectsRender<Icon>(DataProperty, ThicknessProperty, FilledProperty, ForegroundProperty);
    }

    /// <summary>图标颜色，默认继承外层 Foreground。</summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public string? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    /// <summary>实心图标（播放三角、省略号等）。</summary>
    public bool Filled
    {
        get => GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataProperty)
        {
            _geometry = null;
            var text = Data;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    _geometry = Geometry.Parse(text);
                }
                catch
                {
                    _geometry = null;
                }
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        if (_geometry is null) return;

        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var brush = Foreground ?? Brushes.White;
        var scale = Math.Min(w, h) / 24.0;
        var dx = (w - 24 * scale) / 2;
        var dy = (h - 24 * scale) / 2;

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(dx, dy)))
        {
            if (Filled)
            {
                context.DrawGeometry(brush, null, _geometry);
            }
            else
            {
                var pen = new Pen(brush, Thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                context.DrawGeometry(null, pen, _geometry);
            }
        }
    }
}
