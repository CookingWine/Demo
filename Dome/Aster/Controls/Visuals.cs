using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Aster.Controls;

/// <summary>Aster 六瓣花标识。</summary>
public class LogoMark : Control
{
    public static readonly StyledProperty<IBrush?> PetalBrushProperty =
        AvaloniaProperty.Register<LogoMark, IBrush?>(nameof(PetalBrush));

    public static readonly StyledProperty<IBrush?> CoreBrushProperty =
        AvaloniaProperty.Register<LogoMark, IBrush?>(nameof(CoreBrush));

    static LogoMark()
    {
        AffectsRender<LogoMark>(PetalBrushProperty, CoreBrushProperty);
    }

    public IBrush? PetalBrush
    {
        get => GetValue(PetalBrushProperty);
        set => SetValue(PetalBrushProperty, value);
    }

    public IBrush? CoreBrush
    {
        get => GetValue(CoreBrushProperty);
        set => SetValue(CoreBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var brush = PetalBrush ?? new SolidColorBrush(Color.Parse("#0A0B12"));
        var core = CoreBrush ?? new SolidColorBrush(Colors.White, 0.55);
        var k = Math.Min(w, h) / 64.0;

        for (var i = 0; i < 6; i++)
        {
            var m = Matrix.CreateTranslation(-32, -32)
                    * Matrix.CreateScale(k, k)
                    * Matrix.CreateRotation(i * Math.PI / 3)
                    * Matrix.CreateTranslation(w / 2, h / 2);
            using (context.PushTransform(m))
            {
                context.DrawEllipse(brush, null, new Point(0, -11), 6.6, 11.2);
            }
        }

        context.DrawEllipse(brush, null, new Point(w / 2, h / 2), 6.2 * k, 6.2 * k);
        context.DrawEllipse(core, null, new Point(w / 2, h / 2), 2.5 * k, 2.5 * k);
    }
}

/// <summary>音频可视化柱状图（带轻微动画）。</summary>
public class AudioBars : Control
{
    private readonly DispatcherTimer _timer;
    private readonly double[] _levels;
    private readonly double[] _speed;
    private double _t;

    public AudioBars()
    {
        const int count = 18;
        _levels = new double[count];
        _speed = new double[count];
        var rnd = new Random(7);
        for (var i = 0; i < count; i++)
        {
            _levels[i] = 0.25 + rnd.NextDouble() * 0.6;
            _speed[i] = 0.8 + rnd.NextDouble() * 1.4;
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _timer.Tick += (_, _) =>
        {
            _t += 0.09;
            if (IsVisible) InvalidateVisual();
        };
    }

    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<AudioBars, bool>(nameof(IsPlaying), true);

    public bool IsPlaying
    {
        get => GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 4 || h <= 4) return;

        const double barW = 5;
        const double gap = 4;
        var count = _levels.Length;
        var totalW = count * barW + (count - 1) * gap;
        var startX = (w - totalW) / 2;

        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#A78BFA"), 0),
                new GradientStop(Color.Parse("#6C8BFF"), 0.5),
                new GradientStop(Color.Parse("#3DD6E8"), 1)
            }
        };

        for (var i = 0; i < count; i++)
        {
            var wave = Math.Sin(_t * _speed[i] + i * 0.7) * 0.5 + 0.5;
            var level = IsPlaying ? 0.12 + _levels[i] * wave * 0.9 : 0.12 + _levels[i] * 0.1;
            level = Math.Clamp(level, 0.08, 1);

            var barH = Math.Max(4, (h - 8) * level);
            var x = startX + i * (barW + gap);
            var rect = new Rect(x, h - 4 - barH, barW, barH);
            context.DrawRectangle(brush, null, rect, 2.5, 2.5);
        }
    }
}

/// <summary>旋转的黑胶唱片。</summary>
public class SpinningDisc : Control
{
    private readonly DispatcherTimer _timer;
    private double _angle;

    public SpinningDisc()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) =>
        {
            _angle = (_angle + 1.4) % 360;
            if (IsVisible) InvalidateVisual();
        };
    }

    public static readonly StyledProperty<bool> IsSpinningProperty =
        AvaloniaProperty.Register<SpinningDisc, bool>(nameof(IsSpinning), true);

    public bool IsSpinning
    {
        get => GetValue(IsSpinningProperty);
        set => SetValue(IsSpinningProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 4 || h <= 4) return;

        var center = new Point(w / 2, h / 2);
        var r = Math.Min(w, h) / 2;

        using (context.PushTransform(Matrix.CreateRotation(_angle * Math.PI / 180, center)))
        {
            // 盘体
            var body = new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.38, 0.32, RelativeUnit.Relative),
                Radius = 0.55,
                GradientStops =
                {
                    new GradientStop(Color.Parse("#1A1B28"), 0),
                    new GradientStop(Color.Parse("#0D0E16"), 0.62),
                    new GradientStop(Color.Parse("#2A2B3C"), 0.86),
                    new GradientStop(Color.Parse("#12131D"), 1)
                }
            };
            context.DrawEllipse(body, null, center, r, r);

            // 纹路
            var groove = new Pen(new SolidColorBrush(Colors.White, 0.045), 1);
            for (var i = 1; i <= 4; i++)
            {
                var rr = r * (0.55 + i * 0.1);
                context.DrawEllipse(null, groove, center, rr, rr);
            }

            // 中心标签
            var label = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#A78BFA"), 0),
                    new GradientStop(Color.Parse("#3DD6E8"), 1)
                }
            };
            context.DrawEllipse(label, null, center, r * 0.245, r * 0.245);
            context.DrawEllipse(new SolidColorBrush(Color.Parse("#0E0F18")), null, center, r * 0.035, r * 0.035);
        }
    }
}

/// <summary>进度/音量条（可拖动，悬停显示滑块）。</summary>
public class TrackBar : Control
{
    private bool _dragging;

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<TrackBar, double>(nameof(Value), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<TrackBar, double>(nameof(Maximum), 100d);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<TrackBar, double>(nameof(Minimum), 0d);

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<TrackBar, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<double> TrackHeightProperty =
        AvaloniaProperty.Register<TrackBar, double>(nameof(TrackHeight), 4d);

    static TrackBar()
    {
        AffectsRender<TrackBar>(ValueProperty, MaximumProperty, MinimumProperty, FillProperty, TrackHeightProperty);
    }

    public TrackBar()
    {
        Focusable = false;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double TrackHeight
    {
        get => GetValue(TrackHeightProperty);
        set => SetValue(TrackHeightProperty, value);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _dragging = true;
        e.Pointer.Capture(this);
        SetFromPoint(e.GetPosition(this).X);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) SetFromPoint(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        InvalidateVisual();
    }

    private void SetFromPoint(double x)
    {
        var w = Bounds.Width;
        if (w <= 0) return;
        var ratio = Math.Clamp(x / w, 0, 1);
        Value = Minimum + ratio * (Maximum - Minimum);
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 2) return;

        var th = TrackHeight;
        var cy = h / 2;
        var trackRect = new Rect(0, cy - th / 2, w, th);
        context.DrawRectangle(new SolidColorBrush(Colors.White, 0.10), null, trackRect, th / 2, th / 2);

        var span = Maximum - Minimum;
        if (span <= 0) span = 1;
        var ratio = Math.Clamp((Value - Minimum) / span, 0, 1);
        var fillW = w * ratio;
        if (fillW > 0.5)
        {
            var fill = Fill ?? new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#6C8BFF"), 0),
                    new GradientStop(Color.Parse("#A78BFA"), 1)
                }
            };
            context.DrawRectangle(fill, null, new Rect(0, cy - th / 2, fillW, th), th / 2, th / 2);
        }

        if (IsPointerOver || _dragging)
        {
            var cx = Math.Clamp(fillW, 5, w - 5);
            context.DrawEllipse(new SolidColorBrush(Colors.White), null, new Point(cx, cy), 5, 5);
        }
    }
}


/// <summary>三格跳动指示器（正在播放）。</summary>
public class EqIndicator : Control
{
    private readonly DispatcherTimer _timer;
    private double _t;

    public EqIndicator()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _timer.Tick += (_, _) =>
        {
            _t += 0.35;
            if (IsVisible) InvalidateVisual();
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 2 || h <= 2) return;

        var brush = new SolidColorBrush(Color.Parse("#6C8BFF"));
        const double barW = 2;
        var gap = (w - barW * 3) / 2;

        for (var i = 0; i < 3; i++)
        {
            var level = 0.35 + 0.65 * (Math.Sin(_t + i * 1.1) * 0.5 + 0.5);
            var barH = Math.Max(2, h * level);
            context.DrawRectangle(brush, null, new Rect(i * (barW + gap), h - barH, barW, barH), 1, 1);
        }
    }
}
