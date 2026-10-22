// 文件：Charts.cs
// 作用：三个零依赖的图表控件——Sparkline（迷你折线/面积）、TrendChart（双曲线趋势）、DonutChart（环形占比）。
// 使用方：仪表盘与指标卡，XAML 中直接 <ctrl:Sparkline Points="{Binding ...}" Stroke="#6C8BFF"/>。
// 实现：均继承 Control 并重写 Render，用 DrawingContext 直接绘制线段、圆弧与渐变，不引入任何图表库。
// 取舍：只负责静态图形，不处理坐标文字、缩放与交互；数据量在几十点以内，每次全量重建几何完全够用。
// 精度：坐标按 Bounds 实时换算，窗口缩放无需额外处理；线帽/连接统一用 Round 避免尖角锯齿。
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Aster.Controls;

/// <summary>折线/面积迷你图（今日节奏、指标卡 sparkline）。</summary>
// 特点：不显示坐标轴与刻度，只表达趋势，因此把数据线性映射到整个可用高度即可。
// 布局：控件本身不测量，宽高由外部 Width/Height 或容器决定；太小时直接跳过绘制。
public class Sparkline : Control
{
    /// <summary>数据序列；少于 2 个点时无法连线，不绘制。默认 null。</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> PointsProperty =
        AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Points));

    /// <summary>线条与面积渐变的主色，默认 null 时回退到主色 #6C8BFF。</summary>
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke));

    /// <summary>是否绘制下方渐变面积，默认 true。</summary>
    public static readonly StyledProperty<bool> ShowAreaProperty =
        AvaloniaProperty.Register<Sparkline, bool>(nameof(ShowArea), true);

    /// <summary>线条宽度，默认 1.8；同时参与上下留白的计算。</summary>
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(StrokeThickness), 1.8);

    // 四个属性任一变化都自动重绘：包含数据、颜色、是否面积、线宽。
    static Sparkline()
    {
        AffectsRender<Sparkline>(PointsProperty, StrokeProperty, ShowAreaProperty, StrokeThicknessProperty);
    }

    /// <summary>数据序列（按时间先后排列），默认 null。</summary>
    public IReadOnlyList<double>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    /// <summary>线条颜色，默认回退 #6C8BFF。</summary>
    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>是否填充面积渐变，默认 true。</summary>
    public bool ShowArea
    {
        get => GetValue(ShowAreaProperty);
        set => SetValue(ShowAreaProperty, value);
    }

    /// <summary>线条宽度，默认 1.8。</summary>
    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>
    /// 绘制入口：先把数据归一化到 0..1，再映射到去掉留白后的绘制区。
    /// </summary>
    public override void Render(DrawingContext context)
    {
        var pts = Points;
        var w = Bounds.Width;
        var h = Bounds.Height;
        // 少于两个点无法构成线段；宽高过小则没有可用绘制空间。
        if (pts is null || pts.Count < 2 || w <= 2 || h <= 2) return;

        var brush = Stroke ?? new SolidColorBrush(Color.Parse("#6C8BFF"));
        // 先扫一遍求极值，后续把 [min,max] 线性压缩到 [0,1]，这样任意量纲的数据都能铺满控件高度。
        double min = double.MaxValue, max = double.MinValue;
        foreach (var p in pts)
        {
            if (p < min) min = p;
            if (p > max) max = p;
        }
        // range 兜底 0.0001：数据全相等时避免除零，此时曲线会贴在底部（或中部）呈现为一条直线。
        var range = Math.Max(max - min, 0.0001);
        // 上下各留 pad 的空白：线宽有厚度、线帽还是圆的，不留白会被 Bounds 边缘裁掉半个笔触。
        var pad = StrokeThickness + 1;

        // 索引 -> 屏幕坐标：x 按点数等分（首尾贴边），y 用 (值-min)/range 归一化后翻转（屏幕 y 向下）。
        Point At(int i)
        {
            var x = i * w / (pts.Count - 1);
            var y = h - pad - (pts[i] - min) / range * (h - pad * 2);
            return new Point(x, y);
        }

        // 折线用 StreamGeometry 一次构建：BeginFigure 的第二个参数 false 表示不自动闭合。
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(At(0), false);
            for (var i = 1; i < pts.Count; i++) ctx.LineTo(At(i));
            ctx.EndFigure(false);
        }

        if (ShowArea)
        {
            // 面积 = 折线 + 底部两个角，闭合后才有可填充的区域。
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(0, h), true);
                ctx.LineTo(At(0));
                for (var i = 1; i < pts.Count; i++) ctx.LineTo(At(i));
                ctx.LineTo(new Point(w, h));
                ctx.EndFigure(true);
            }

            // 自上而下的线性渐变：顶部 0.38 不透明度，到控件底部完全透明，制造「光晕沉入背景」的效果。
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

        // 先画面再画线：面积在下层，折线覆盖其上，避免渐变把线条颜色冲淡。
        var pen = new Pen(brush, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        context.DrawGeometry(null, pen, line);
    }

    /// <summary>
    /// 取画刷的颜色并替换 alpha，用于生成同色系渐变；非纯色画刷（渐变/图片）时回退主色。
    /// </summary>
    private static Color WithAlpha(IBrush brush, double alpha)
    {
        var c = brush is ISolidColorBrush scb ? scb.Color : Color.Parse("#6C8BFF");
        return Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);
    }
}

/// <summary>访问与转化趋势图：网格 + 面积 + 两条曲线 + 数据点。</summary>
// 视觉分层（由下到上）：网格线 -> 主曲线面积渐变 -> 主折线 -> 数据点 -> 次曲线虚线。
// 主轴与次轴共用同一套 min/max，因此两条曲线的相对高低在视觉上可直接比较。
public class TrendChart : Control
{
    /// <summary>主曲线数据（通常是访问量），默认 null；不足 2 点时整张图只画网格。</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> PrimaryProperty =
        AvaloniaProperty.Register<TrendChart, IReadOnlyList<double>?>(nameof(Primary));

    /// <summary>次曲线数据（通常是转化数），默认 null，可选。</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> SecondaryProperty =
        AvaloniaProperty.Register<TrendChart, IReadOnlyList<double>?>(nameof(Secondary));

    /// <summary>主曲线颜色，默认回退 #6C8BFF。</summary>
    public static readonly StyledProperty<IBrush?> PrimaryBrushProperty =
        AvaloniaProperty.Register<TrendChart, IBrush?>(nameof(PrimaryBrush));

    /// <summary>次曲线颜色，默认回退 #A78BFA。</summary>
    public static readonly StyledProperty<IBrush?> SecondaryBrushProperty =
        AvaloniaProperty.Register<TrendChart, IBrush?>(nameof(SecondaryBrush));

    // 数据或配色变化都会重绘；网格不依赖数据，但代码简单起见一并重画。
    static TrendChart()
    {
        AffectsRender<TrendChart>(PrimaryProperty, SecondaryProperty, PrimaryBrushProperty, SecondaryBrushProperty);
    }

    /// <summary>主曲线数据。</summary>
    public IReadOnlyList<double>? Primary
    {
        get => GetValue(PrimaryProperty);
        set => SetValue(PrimaryProperty, value);
    }

    /// <summary>次曲线数据（虚线）。</summary>
    public IReadOnlyList<double>? Secondary
    {
        get => GetValue(SecondaryProperty);
        set => SetValue(SecondaryProperty, value);
    }

    /// <summary>主曲线颜色，默认 #6C8BFF。</summary>
    public IBrush? PrimaryBrush
    {
        get => GetValue(PrimaryBrushProperty);
        set => SetValue(PrimaryBrushProperty, value);
    }

    /// <summary>次曲线颜色，默认 #A78BFA。</summary>
    public IBrush? SecondaryBrush
    {
        get => GetValue(SecondaryBrushProperty);
        set => SetValue(SecondaryBrushProperty, value);
    }

    /// <summary>
    /// 绘制入口：网格 -> 面积 -> 主折线与数据点 -> 次曲线。
    /// </summary>
    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        // 尺寸太小画出来只有噪点，直接跳过。
        if (w <= 8 || h <= 8) return;

        var primary = PrimaryBrush ?? new SolidColorBrush(Color.Parse("#6C8BFF"));
        var secondary = SecondaryBrush ?? new SolidColorBrush(Color.Parse("#A78BFA"));

        // 网格
        // 4 条等距水平线：上下各留 20px，剩余高度三等分，因此间距是 (h-40)/3。
        // 用浮点 3.0 而非整数 3，避免整数除法把间距截断导致网格挤在上方。
        var gridPen = new Pen(new SolidColorBrush(Colors.White, 0.05), 1);
        for (var i = 0; i < 4; i++)
        {
            var y = 20 + i * (h - 40) / 3.0;
            context.DrawLine(gridPen, new Point(0, y), new Point(w, y));
        }

        // 没有主数据时保留网格作为占位，避免图表区域变成空白。
        if (Primary is not { Count: > 1 } p) return;

        // 绘图区内边距：左侧 22 给「未来可能的 Y 轴刻度」留位，右侧 12 防止末点被裁，上下各 14。
        const double left = 22;
        var right = w - 12;
        var top = 14.0;
        var bottom = h - 14;

        // 主次两条曲线共用同一套极值：这样两条线的相对高度才有可比性，
        // 若各自归一化，量级相差很大的两条线会被压成同样的波形。
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

        // 索引 -> 坐标：x 在 [left,right] 上按各自点数等分，y 翻转（值越大越靠上）。
        Point Map(int i, double v, int count)
        {
            var x = left + i * (right - left) / (count - 1);
            var y = bottom - (v - min) / range * (bottom - top);
            return new Point(x, y);
        }

        // 面积
        // 闭合路径 = 左下角 -> 沿曲线 -> 右下角，填充时才不会漏底。
        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(Map(0, p[0], p.Count).X, bottom), true);
            for (var i = 0; i < p.Count; i++) ctx.LineTo(Map(i, p[i], p.Count));
            ctx.LineTo(new Point(Map(p.Count - 1, p[^1], p.Count).X, bottom));
            ctx.EndFigure(true);
        }
        var color = primary is ISolidColorBrush scb ? scb.Color : Color.Parse("#6C8BFF");
        // alpha 82/255 ≈ 0.32：比 Sparkline 的 0.38 稍淡，因为大图面积更大，太浓会压住网格。
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

        // 数据点：先填深底 #13161D 再描边，形成「挖空」效果，让点能从面积渐变上跳出来；
        // 半径 3 与 2px 描边配合，视觉直径约 8px，正好是设计稿的圆点大小。
        foreach (var pt in linePts)
        {
            context.DrawEllipse(new SolidColorBrush(Color.Parse("#13161D")), new Pen(primary, 2), pt, 3, 3);
        }

        // 次曲线（虚线）
        // DashStyle(4,4) 表示 4px 实线 + 4px 空白；虚线不面积填充，避免两条半透明面积互相叠加变脏。
        if (Secondary is { Count: > 1 } s)
        {
            var pts = new List<Point>();
            for (var i = 0; i < s.Count; i++) pts.Add(Map(i, s[i], s.Count));
            DrawPolyline(context, pts,
                new Pen(secondary, 1.8, new DashStyle(new double[] { 4, 4 }, 0),
                    lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round));
        }
    }

    /// <summary>
    /// 取画刷颜色并替换 alpha；当前未直接调用，保留为同色系配色工具。
    /// </summary>
    private static Color WithAlpha(IBrush brush, double alpha)
    {
        var c = brush is ISolidColorBrush scb ? scb.Color : Color.Parse("#6C8BFF");
        return Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);
    }

    /// <summary>
    /// 把点列串成一条不闭合的折线：BeginFigure 传 false 避免首尾相连，EndFigure(false) 不自动闭合。
    /// </summary>
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
// 实现：先用一整圈半透明底色画出轨道，再按占比逐段叠加彩色圆弧；
// 每段只画「弧线」并用与环宽等厚的 Pen 描边，因此不需要构造内外两条边组成的闭合环。
public class DonutChart : Control
{
    /// <summary>各段数值（无需预先归一化，内部按总和换算占比），默认 null。</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<DonutChart, IReadOnlyList<double>?>(nameof(Values));

    /// <summary>自定义分段颜色，默认 null 时使用主色/紫/青/绿的固定调色板并循环取用。</summary>
    public static readonly StyledProperty<IReadOnlyList<Color>?> ColorsProperty =
        AvaloniaProperty.Register<DonutChart, IReadOnlyList<Color>?>(nameof(Colors));

    /// <summary>圆环宽度，默认 12；半径会自动扣除一半宽度，保证外沿贴住控件边界。</summary>
    public static readonly StyledProperty<double> RingThicknessProperty =
        AvaloniaProperty.Register<DonutChart, double>(nameof(RingThickness), 12);

    // 数值、配色、环宽都会影响几何，统一登记为影响渲染的属性。
    static DonutChart()
    {
        AffectsRender<DonutChart>(ValuesProperty, ColorsProperty, RingThicknessProperty);
    }

    /// <summary>各段数值，默认 null。</summary>
    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>分段颜色表，默认 null 时使用内置调色板。</summary>
    public IReadOnlyList<Color>? Colors
    {
        get => GetValue(ColorsProperty);
        set => SetValue(ColorsProperty, value);
    }

    /// <summary>圆环宽度，默认 12。</summary>
    public double RingThickness
    {
        get => GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    /// <summary>
    /// 绘制入口：轨道 -> 按累计角度逐段绘制圆弧。
    /// </summary>
    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 8 || h <= 8) return;

        // 取 min(w,h) 保证圆环在任何非方形区域里都不被拉成椭圆。
        var size = Math.Min(w, h);
        var thickness = RingThickness;
        // 半径取「外沿 - 半个环宽」，因为描边以路径为中心向两侧各扩展 thickness/2。
        var radius = size / 2 - thickness / 2;
        var center = new Point(w / 2, h / 2);

        // 底层轨道：白色 6% 透明度，让空数据时也有一个可见的环形占位。
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Avalonia.Media.Colors.White, 0.06), thickness), center, radius, radius);

        var values = Values;
        var colors = Colors;
        if (values is null || values.Count == 0) return;

        // 先求和再逐段换算占比；总和为 0 或负数时没有可画的扇形，直接返回。
        var total = 0.0;
        foreach (var v in values) total += v;
        if (total <= 0) return;

        // 设计令牌调色板：主色 -> 紫 -> 青 -> 绿，超出长度后取模循环。
        var palette = new[]
        {
            Color.Parse("#6C8BFF"), Color.Parse("#A78BFA"), Color.Parse("#3DD6E8"), Color.Parse("#3DDC97")
        };

        // 从 -90° 起画：屏幕坐标 0° 指向 3 点方向，-90° 才是 12 点方向，视觉上从正上方开始顺时针排布。
        var start = -90.0;
        for (var i = 0; i < values.Count; i++)
        {
            var sweep = values[i] / total * 360.0;
            // 占比极小的段直接跳过：既看不见，又会因为只剩 1.5° 间隙而画成一个小点。
            // 注意 continue 会连带跳过下面的 start += sweep，被跳过的角度不会补回，
            // 后续分段的起点会略微前移；对整体观感没有影响，但严格来说首尾不会精确闭合。
            if (sweep <= 0.1) continue;
            var color = colors is not null && i < colors.Count ? colors[i] : palette[i % palette.Length];
            // 每段末尾少画 1.5°，形成分段之间的空隙；间隙从本段的角度里扣，因此后面的累计起点仍按完整 sweep 前进。
            // 占比刚过 0.1° 的段扣除间隙后 sweep 可能为负，会画出一小段反向弧；真实数据下极少出现。
            var geo = ArcGeometry(center, radius, start, sweep - 1.5);
            context.DrawGeometry(null, new Pen(new SolidColorBrush(color), thickness, lineCap: PenLineCap.Round), geo);
            start += sweep;
        }
    }

    /// <summary>
    /// 把「起始角 + 扫过角」换算成一段圆弧几何（角度制，屏幕坐标系顺时针为正）。
    /// </summary>
    private static Geometry ArcGeometry(Point center, double radius, double startAngle, double sweepAngle)
    {
        // 角度转弧度：三角函数需要弧度，且 y 轴向下，正角自然表现为顺时针。
        var a0 = startAngle * Math.PI / 180;
        var a1 = (startAngle + sweepAngle) * Math.PI / 180;
        var p0 = new Point(center.X + radius * Math.Cos(a0), center.Y + radius * Math.Sin(a0));
        var p1 = new Point(center.X + radius * Math.Cos(a1), center.Y + radius * Math.Sin(a1));

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(p0, false);
            // ArcTo 参数：终点、椭圆半径、旋转角、largeArc、sweepDirection。
            // largeArc 必须按扫过角是否大于 180° 决定，否则超过半圈时 SVG 会选「短的那条弧」，画出的段会反向或缺失；
            // Clockwise 与角度增大的方向一致（屏幕 y 向下），这样圆弧才顺着累计角度延伸。
            ctx.ArcTo(p1, new Size(radius, radius), 0, sweepAngle > 180,
                SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        return geo;
    }
}
