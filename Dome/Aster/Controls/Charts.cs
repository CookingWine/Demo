using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Aster.Controls;

/// <summary>折线/面积迷你图（今日节奏、指标卡 sparkline）。</summary>
public class Sparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> PointsProperty =
        AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Points));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<bool> ShowAreaProperty =
        AvaloniaProperty.Register<Sparkline, bool>(nameof(ShowArea), true);

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(StrokeThickness), 1.8);

    static Sparkline()
    {
        AffectsRender<Sparkline>(PointsProperty, StrokeProperty, ShowAreaProperty, StrokeThicknessProperty);
    }

    public IReadOnlyList<double>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public bool ShowArea
    {
        get => GetValue(ShowAreaProperty);
        set => SetValue(ShowAreaProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var pts = Points;
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (pts is null || pts.Count < 2 || w <= 2 || h <= 2) return;

        var brush = Stroke ?? new SolidColorBrush(Color.Parse("#6C8BFF"));
        double min = double.MaxValue, max = double.MinValue;
        foreach (var p in pts)
        {
            if (p < min) min = p;
            if (p > max) max = p;
        }
        var range = Math.Max(max - min, 0.0001);
        var pad = StrokeThickness + 1;

        Point At(int i)
        {
            var x = i * w / (pts.Count - 1);
            var y = h - pad - (pts[i] - min) / range * (h - pad * 2);
            return new Point(x, y);
        }

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(At(0), false);
            for (var i = 1; i < pts.Count; i++) ctx.LineTo(At(i));
            ctx.EndFigure(false);
        }

        if (ShowArea)
        {
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(0, h), true);
                ctx.LineTo(At(0));
                for (var i = 1; i < pts.Count; i++) ctx.LineTo(At(i));
                ctx.LineTo(new Point(w, h));
                ctx.EndFigure(true);
            }

            var grad = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(WithAlpha(brush, 0.38), 0),
                    new GradientStop(WithAlpha(brush, 0), 1)
                }
            };
            context.DrawGeometry(grad, null, area);
        }

        var pen = new Pen(brush, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        context.DrawGeometry(null, pen, line);
    }

    private static Color WithAlpha(IBrush brush, double alpha)
    {
        var c = brush is ISolidColorBrush scb ? scb.Color : Color.Parse("#6C8BFF");
        return Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);
    }
}

/// <summary>访问与转化趋势图：网格 + 面积 + 两条曲线 + 数据点。</summary>
public class TrendChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> PrimaryProperty =
        AvaloniaProperty.Register<TrendChart, IReadOnlyList<double>?>(nameof(Primary));

    public static readonly StyledProperty<IReadOnlyList<double>?> SecondaryProperty =
        AvaloniaProperty.Register<TrendChart, IReadOnlyList<double>?>(nameof(Secondary));

    public static readonly StyledProperty<IBrush?> PrimaryBrushProperty =
        AvaloniaProperty.Register<TrendChart, IBrush?>(nameof(PrimaryBrush));

    public static readonly StyledProperty<IBrush?> SecondaryBrushProperty =
        AvaloniaProperty.Register<TrendChart, IBrush?>(nameof(SecondaryBrush));

    static TrendChart()
    {
        AffectsRender<TrendChart>(PrimaryProperty, SecondaryProperty, PrimaryBrushProperty, SecondaryBrushProperty);
    }

    public IReadOnlyList<double>? Primary
    {
        get => GetValue(PrimaryProperty);
        set => SetValue(PrimaryProperty, value);
    }

    public IReadOnlyList<double>? Secondary
    {
        get => GetValue(SecondaryProperty);
        set => SetValue(SecondaryProperty, value);
    }

    public IBrush? PrimaryBrush
    {
        get => GetValue(PrimaryBrushProperty);
        set => SetValue(PrimaryBrushProperty, value);
    }

    public IBrush? SecondaryBrush
    {
        get => GetValue(SecondaryBrushProperty);
        set => SetValue(SecondaryBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 8 || h <= 8) return;

        var primary = PrimaryBrush ?? new SolidColorBrush(Color.Parse("#6C8BFF"));
        var secondary = SecondaryBrush ?? new SolidColorBrush(Color.Parse("#A78BFA"));

        // 网格
        var gridPen = new Pen(new SolidColorBrush(Colors.White, 0.05), 1);
        for (var i = 0; i < 4; i++)
        {
            var y = 20 + i * (h - 40) / 3.0;
            context.DrawLine(gridPen, new Point(0, y), new Point(w, y));
        }

        if (Primary is not { Count: > 1 } p) return;

        const double left = 22;
        var right = w - 12;
        var top = 14.0;
        var bottom = h - 14;

        double min = double.MaxValue, max = double.MinValue;
        foreach (var v in p)
        {
            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }
        if (Secondary is { Count: > 1 } s0)
        {
            foreach (var v in s0)
            {
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
        }
        var range = Math.Max(max - min, 0.0001);

        Point Map(int i, double v, int count)
        {
            var x = left + i * (right - left) / (count - 1);
            var y = bottom - (v - min) / range * (bottom - top);
            return new Point(x, y);
        }

        // 面积
        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(Map(0, p[0], p.Count).X, bottom), true);
            for (var i = 0; i < p.Count; i++) ctx.LineTo(Map(i, p[i], p.Count));
            ctx.LineTo(new Point(Map(p.Count - 1, p[^1], p.Count).X, bottom));
            ctx.EndFigure(true);
        }
        var color = primary is ISolidColorBrush scb ? scb.Color : Color.Parse("#6C8BFF");
        var grad = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(82, color.R, color.G, color.B), 0),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1)
            }
        };
        context.DrawGeometry(grad, null, area);

        // 主曲线
        var linePts = new List<Point>();
        for (var i = 0; i < p.Count; i++) linePts.Add(Map(i, p[i], p.Count));
        DrawPolyline(context, linePts, new Pen(primary, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round));

        foreach (var pt in linePts)
        {
            context.DrawEllipse(new SolidColorBrush(Color.Parse("#13161D")), new Pen(primary, 2), pt, 3, 3);
        }

        // 次曲线（虚线）
        if (Secondary is { Count: > 1 } s)
        {
            var pts = new List<Point>();
            for (var i = 0; i < s.Count; i++) pts.Add(Map(i, s[i], s.Count));
            DrawPolyline(context, pts,
                new Pen(secondary, 1.8, new DashStyle(new double[] { 4, 4 }, 0),
                    lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round));
        }
    }

    private static Color WithAlpha(IBrush brush, double alpha)
    {
        var c = brush is ISolidColorBrush scb ? scb.Color : Color.Parse("#6C8BFF");
        return Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);
    }

    private static void DrawPolyline(DrawingContext context, IReadOnlyList<Point> pts, IPen pen)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(pts[0], false);
            for (var i = 1; i < pts.Count; i++) ctx.LineTo(pts[i]);
            ctx.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geo);
    }
}

/// <summary>环形图（渠道构成）。</summary>
public class DonutChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<DonutChart, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<IReadOnlyList<Color>?> ColorsProperty =
        AvaloniaProperty.Register<DonutChart, IReadOnlyList<Color>?>(nameof(Colors));

    public static readonly StyledProperty<double> RingThicknessProperty =
        AvaloniaProperty.Register<DonutChart, double>(nameof(RingThickness), 12);

    static DonutChart()
    {
        AffectsRender<DonutChart>(ValuesProperty, ColorsProperty, RingThicknessProperty);
    }

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IReadOnlyList<Color>? Colors
    {
        get => GetValue(ColorsProperty);
        set => SetValue(ColorsProperty, value);
    }

    public double RingThickness
    {
        get => GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 8 || h <= 8) return;

        var size = Math.Min(w, h);
        var thickness = RingThickness;
        var radius = size / 2 - thickness / 2;
        var center = new Point(w / 2, h / 2);

        context.DrawEllipse(null, new Pen(new SolidColorBrush(Avalonia.Media.Colors.White, 0.06), thickness), center, radius, radius);

        var values = Values;
        var colors = Colors;
        if (values is null || values.Count == 0) return;

        var total = 0.0;
        foreach (var v in values) total += v;
        if (total <= 0) return;

        var palette = new[]
        {
            Color.Parse("#6C8BFF"), Color.Parse("#A78BFA"), Color.Parse("#3DD6E8"), Color.Parse("#3DDC97")
        };

        var start = -90.0;
        for (var i = 0; i < values.Count; i++)
        {
            var sweep = values[i] / total * 360.0;
            if (sweep <= 0.1) continue;
            var color = colors is not null && i < colors.Count ? colors[i] : palette[i % palette.Length];
            var geo = ArcGeometry(center, radius, start, sweep - 1.5);
            context.DrawGeometry(null, new Pen(new SolidColorBrush(color), thickness, lineCap: PenLineCap.Round), geo);
            start += sweep;
        }
    }

    private static Geometry ArcGeometry(Point center, double radius, double startAngle, double sweepAngle)
    {
        var a0 = startAngle * Math.PI / 180;
        var a1 = (startAngle + sweepAngle) * Math.PI / 180;
        var p0 = new Point(center.X + radius * Math.Cos(a0), center.Y + radius * Math.Sin(a0));
        var p1 = new Point(center.X + radius * Math.Cos(a1), center.Y + radius * Math.Sin(a1));

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(p0, false);
            ctx.ArcTo(p1, new Size(radius, radius), 0, sweepAngle > 180,
                SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        return geo;
    }
}
