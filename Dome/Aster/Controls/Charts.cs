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
        // 前置状态：Points 由 XAML 绑定进来，可能为 null（数据未加载）、可能只有 1 个点（区间还没攒够）。
        // 后置状态：本方法不改任何字段，只提交绘制指令；没有 IDisposable / 动画，纯一次性出图。
        // 触发者：AffectsRender 登记的四个属性任一变化 -> InvalidateVisual -> 下一帧调用本方法。
        var pts = Points;
        var w = Bounds.Width;
        var h = Bounds.Height;
        // 少于两个点无法构成线段；宽高过小则没有可用绘制空间。
        // 宽高阈值 2 不是随手取的：下面 pad = StrokeThickness + 1 ≈ 2.8，
        // 当 h <= 2 时 h - 2*pad 会变成负数，映射出来的 y 会翻到控件外面。
        if (pts is null || pts.Count < 2 || w <= 2 || h <= 2) return;

        // 步骤 1：确定画刷。Stroke 为 null 时回退品牌主色 #6C8BFF（与整套 UI 的主色令牌一致）。
        var brush = Stroke ?? new SolidColorBrush(Color.Parse("#6C8BFF"));
        // 步骤 2：求极值，为归一化做准备。
        // 先扫一遍求极值，后续把 [min,max] 线性压缩到 [0,1]，这样任意量纲的数据都能铺满控件高度。
        double min = double.MaxValue, max = double.MinValue;
        foreach (var p in pts)
        {
            if (p < min) min = p;
            if (p > max) max = p;
        }
        // range 兜底 0.0001：数据全相等时避免除零，此时曲线会贴在底部（或中部）呈现为一条直线。
        // 更正上一句里的「或中部」：数据全相等时 min == max，(v-min) 恒为 0，
        // 归一化结果 t = 0/0.0001 = 0，代入下面的 y 公式得到 y = h - pad，
        // 也就是曲线一定贴在绘制区**底部**，不会停在中间。只有当所有值恰好等于 0、且
        // min 保持初始的 double.MaxValue 这种极端情况才可能不同，而那种情况在这里不会出现。
        var range = Math.Max(max - min, 0.0001);
        // 步骤 3：算上下留白 pad。
        //   pad = StrokeThickness + 1 的来历，从「描边是怎么画的」推：
        //     1) Pen 的宽度以路径为中心，向两侧各扩 StrokeThickness/2，默认 1.8 即 0.9；
        //     2) 线帽是 Round，末端再多出半径 = StrokeThickness/2 的 0.9；
        //     3) 抗锯齿还需要约 0.5px 的过渡带。
        //   所以理论下限是 StrokeThickness/2 + 0.5 ≈ 1.4，这里取 StrokeThickness + 1 = 2.8，
        //   相当于在线条外沿之外再留约 1.9px，属于偏保守的选择。
        //   代价是有效绘图高度少了 2*pad ≈ 5.6px：控件只有 40 高时，波形实际只占 11.6px。
        // 上下各留 pad 的空白：线宽有厚度、线帽还是圆的，不留白会被 Bounds 边缘裁掉半个笔触。
        var pad = StrokeThickness + 1;

        // 步骤 4：定义「索引 -> 屏幕坐标」的局部函数。
        // 写成局部函数而不是另开方法，是因为它要读 pts / w / h / pad / min / range 六个外部变量，
        // 用闭包最省事（代价是每次 Render 会分配一个编译器生成的闭包对象，几十点的量级无所谓）。
        //
        // x 的公式：x = i * w / (count - 1)，i 从 0 取到 count-1
        //   -> i=0 时 x=0（贴左边），i=count-1 时 x=w（贴右边）。
        //   分母是 count-1 而不是 count：n 个点之间只有 n-1 段间距，
        //   除以 count 会让最后一个点停在 w*(n-1)/n，右侧永远空一条缝。
        //   例：count=7、w=200 -> 步长 200/6 ≈ 33.33，
        //       七个点的 x = 0, 33.3, 66.7, 100, 133.3, 166.7, 200。
        //
        // y 的公式：y = h - pad - ((v - min) / range * (h - 2*pad))
        //   拆成三块看：
        //     t = (v - min) / range       把数据压缩到 [0,1]，min 对应 0、max 对应 1；
        //     t * (h - 2*pad)             把这个比例换算成「绘制区高度」上的像素距离；
        //     h - pad - 上面的结果        从绘制区底边往上量（屏幕 y 向下，所以要减）。
        //   边界验证：
        //     t=0   -> y = h - pad        （最小值落在绘制区底边）
        //     t=1   -> y = h - pad - (h - 2*pad) = pad   （最大值落在绘制区顶边）
        //   正好用满 [pad, h-pad] 这段可用高度，上下各留出的 pad 就是给线宽和圆头线帽的。
        //
        //   完整算例：pts = {3, 8, 5, 10}，w = 200，h = 60，StrokeThickness = 1.8
        //     pad = 2.8，h - 2*pad = 54.4，min = 3，max = 10，range = 7，步长 = 200/3 ≈ 66.67
        //     i=0, v=3 ：x = 0，    t = 0/7    = 0，    y = 57.2 - 0      = 57.2（底边）
        //     i=1, v=8 ：x = 66.7， t = 5/7    ≈ 0.714，y = 57.2 - 38.86  = 18.34
        //     i=2, v=5 ：x = 133.3，t = 2/7    ≈ 0.286，y = 57.2 - 15.54  = 41.66
        //     i=3, v=10：x = 200，  t = 7/7    = 1，    y = 57.2 - 54.4   = 2.8（顶边）
        //   把这四个点在纸上标出来连成折线，就是最终画面上那条线，可以对着一张截图逐个数值核对。
        // 索引 -> 屏幕坐标：x 按点数等分（首尾贴边），y 用 (值-min)/range 归一化后翻转（屏幕 y 向下）。
        Point At(int i)
        {
            var x = i * w / (pts.Count - 1);
            var y = h - pad - (pts[i] - min) / range * (h - pad * 2);
            return new Point(x, y);
        }

        // 步骤 5：构建折线几何。
        // 折线用 StreamGeometry 一次构建：BeginFigure 的第二个参数 false 表示不自动闭合。
        // 这里每个 Render 都 new 一个 StreamGeometry 而不是缓存：数据是外部绑定的 IReadOnlyList，
        // 没有变更通知，无法判断「几何是否过期」，全量重建是唯一正确的做法。
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(At(0), false);
            // 从第 1 个点开始循环（第 0 个已经作为起点），共调用 count-1 次 LineTo。
            for (var i = 1; i < pts.Count; i++) ctx.LineTo(At(i));
            ctx.EndFigure(false);
        }

        // 步骤 6：先画面积（下层），再画折线（上层）。
        if (ShowArea)
        {
            // 面积 = 折线 + 底部两个角，闭合后才有可填充的区域。
            // 为什么第一句是 LineTo(new Point(0, h)) 而不是直接从 At(0) 开始：
            //   填充需要一条闭合轮廓，EndFigure(true) 的闭合动作是「从当前点连回起点」。
            //   若起点写 At(0)，闭合线就会从 (w,h) 斜着连回 At(0)，把曲线右下角切掉一块三角形；
            //   把起点定在左下角 (0,h)，闭合线就正好沿着底边从 (w,h) 直线回到 (0,h)，
            //   围出的区域才是「曲线与底边之间」的那块面积。
            // 轮廓顺序：左下角 (0,h) -> 首点 At(0) -> 各数据点 -> 右下角 (w,h) -> 自动闭合回 (0,h)。
            // 注意底边用的是 h 而不是 h - pad：面积要一直铺到控件最下沿，
            // 与折线保留 pad 留白的处理刻意不同（面积没有线宽，不需要防裁切）。
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
            //   两个 stop 的 alpha 走向：0.38 -> 0，即从「四成不透明」线性淡到全透明。
            //   0.38 * 255 ≈ 96.9，WithAlpha 里四舍五入成 97，所以实际 ARGB 的 A 通道是 97 而不是 96。
            //   若改成 1 -> 0，面积会和折线一样实，整张图变成色块；改成 0.15 又几乎看不见。
            //
            //   坐标是 RelativeUnit.Relative，含义是「相对被填充几何的包围盒」，不是相对控件：
            //   这块面积几何的包围盒顶部是「最高的那个数据点」的 y，底部是 h，
            //   所以最浓的 0.38 出现在曲线最高处而不是控件顶边 —— 视觉上反而更合理，
            //   光晕是从峰值往下沉的。
            //
            //   另注：WithAlpha 用的是 brush（即 Stroke）的颜色；若 Stroke 传入的是渐变画刷，
            //   WithAlpha 会判断出不是 ISolidColorBrush 并回退成 #6C8BFF，
            //   于是线与面积的颜色可能不一致。当前所有调用点传的都是纯色，所以不会触发。
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

        // 步骤 7：最后画折线。
        // 先画面再画线：面积在下层，折线覆盖其上，避免渐变把线条颜色冲淡。
        // 绘制顺序就是图层顺序（后画的盖住先画的），这里没有引入任何 ZIndex 概念。
        // 横向没有留 pad：首末点的 x 正好是 0 和 w，圆头线帽会各往外探出约 0.9px。
        // 因为 Sparkline 默认不裁剪，这点溢出会画到控件边界之外（通常被父容器裁掉，看不出问题）。
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
        // 调用链与 Sparkline 相同：AffectsRender 登记的四个属性任一变化 -> InvalidateVisual -> 本方法。
        // 前置状态：Primary / Secondary 都可能是 null，颜色也可能没设。
        // 后置状态：不写字段，只提交绘制；本方法有两条提前 return 出口——
        //   1) 尺寸过小（连网格都不画）；
        //   2) 没有主数据（画完网格就返回，图表区保留网格作为占位）。
        var w = Bounds.Width;
        var h = Bounds.Height;
        // 尺寸太小画出来只有噪点，直接跳过。
        // 阈值 8 比 Sparkline 的 2 大：这里的网格线本身就要 4 条，高度不足 8 时四条线会叠在一起。
        if (w <= 8 || h <= 8) return;

        // 步骤 1：解析两条曲线的颜色，缺省值就是品牌主色与紫色。
        var primary = PrimaryBrush ?? new SolidColorBrush(Color.Parse("#6C8BFF"));
        var secondary = SecondaryBrush ?? new SolidColorBrush(Color.Parse("#A78BFA"));

        // 步骤 2：画 4 条水平网格线。
        // 网格
        // 4 条等距水平线：上下各留 20px，剩余高度三等分，因此间距是 (h-40)/3。
        // 用浮点 3.0 而非整数 3，避免整数除法把间距截断导致网格挤在上方。
        //
        // 把 y 的算式展开（i = 0,1,2,3）：
        //   i=0 -> y = 20                    （第一条，距顶 20）
        //   i=1 -> y = 20 + (h-40)/3
        //   i=2 -> y = 20 + 2*(h-40)/3
        //   i=3 -> y = 20 + (h-40) = h - 20  （最后一条，距底 20）
        // 相邻间距恒为 (h-40)/3，四条线把「去掉上下各 20」的区域四等分。
        // 算例 h=160：y = 20、66.67、113.33、140，间距 40。
        // 若写成 i * (h-40) / 3（整数 3），当 h-40 不能被 3 整除时先截断再乘，
        // 三条间距会不相等，视觉上网格「挤在上面」。
        // 注意四条线的 x 从 0 拉到 w（整幅宽），而下面的数据只画在 left..right 之间，
        // 所以网格线左侧会比曲线多伸出 22px —— 那块留白是给 Y 轴刻度预留的位置。
        var gridPen = new Pen(new SolidColorBrush(Colors.White, 0.05), 1);
        for (var i = 0; i < 4; i++)
        {
            var y = 20 + i * (h - 40) / 3.0;
            context.DrawLine(gridPen, new Point(0, y), new Point(w, y));
        }

        // 没有主数据时保留网格作为占位，避免图表区域变成空白。
        if (Primary is not { Count: > 1 } p) return;

        // 步骤 3：定义绘图区（四个方向的内边距）。
        //
        // 绘图区内边距：左侧 22 给「未来可能的 Y 轴刻度」留位，右侧 12 防止末点被裁，上下各 14。
        //   left  = 22         曲线从 x=22 才开始，左边 22px 是空的（网格线会穿过去），
        //                      这块位置预留给 Y 轴数字；当前版本没有画刻度，所以看起来是纯留白。
        //   right = w - 12     末点画在 w-12。为什么不是 w：数据点是一个半径 3 的圆 +
        //                      2px 描边（详见步骤 6），外沿半径是 3 + 1 = 4，加上抗锯齿约 1px，
        //                      12 的余量足够放下它，不会被控件边界切掉。
        //   top   = 14         最高点在 y=14，头顶同样要留出圆点的半径 4，否则会被削平。
        //   bottom= h - 14     最低点在 y=h-14，脚下也要留 4 以上。
        //   于是可用绘图区是 [22, w-12] x [14, h-14]。
        //   算例 w=400、h=160：绘图区 x 22..388（宽 366）、y 14..146（高 132）。
        //
        // 顺带一个与直觉不符的地方：网格线的纵向范围是 20..h-20，
        // 而数据是 14..h-14，两者差了 6px。
        // 结果是「最高点会画在第一条网格线上方 6px，最低点会画到最后一条网格线下方 6px」，
        // 数据点看起来会略微溢出网格框。如果希望曲线严格贴合网格，
        // 应该把 top/bottom 改成与网格一致的 20 / h-20。
        const double left = 22;
        var right = w - 12;
        var top = 14.0;
        var bottom = h - 14;

        // 步骤 4：求两条曲线的公共极值。
        // 主次两条曲线共用同一套极值：这样两条线的相对高度才有可比性，
        // 若各自归一化，量级相差很大的两条线会被压成同样的波形。
        // 具体做法：先扫主曲线，若有次曲线则再把它并进来，min 取更小、max 取更大，
        // 相当于把两条曲线放进同一个「值域容器」再一起映射。
        // 副作用（中性，但要知道）：如果次曲线量级远小于主曲线（比如转化数只有访问量的 1%），
        // 它会被压成贴近底边的一条近乎水平的虚线 —— 这是「共用值域」的必然代价，
        // 想要两条线各自占满高度就得引入双 Y 轴。
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

        // 步骤 5：定义映射函数 Map(索引, 值, 点数) -> 屏幕坐标。
        // 索引 -> 坐标：x 在 [left,right] 上按各自点数等分，y 翻转（值越大越靠上）。
        //
        // x = left + i * (right - left) / (count - 1)
        //   · i=0        -> x = left（贴着左内边距，不是控件左边 0）
        //   · i=count-1  -> x = left + (right-left) = right（贴着右内边距）
        //   · 分母 count-1 的理由同 Sparkline：n 个点只有 n-1 段间距。
        //   算例：w=400 -> left=22、right=388、宽 366；count=7 -> 步长 366/6 = 61
        //         x = 22, 83, 144, 205, 266, 327, 388
        //   注意 count 是「调用者传进来的点数」，主次两条曲线的点数可以不同：
        //   主曲线 7 个点、次曲线 5 个点时，各自的步长不同（61 vs 91.5），
        //   但都铺满同一段 22..388，所以两条线的首尾永远对齐、中间的点不对齐。
        //   这是刻意取舍：时间轴刻度由主曲线决定，次曲线只是「形状参考」。
        //
        // y = bottom - ((v - min) / range * (bottom - top))
        //   和 Sparkline 的 y 同一个套路，只是留白换成了 top/bottom：
        //     t = (v-min)/range ∈ [0,1]
        //     t * (bottom-top) = 在绘图区高度上的像素距离
        //     bottom - 它      = 从绘图区底边往上量（屏幕 y 向下）
        //   边界：v=min -> y=bottom=h-14；v=max -> y=top=14。
        //   算例（接着上面的左图）：bottom=146、top=14、高 132；若 t=0.5 则 y = 146-66 = 80。
        Point Map(int i, double v, int count)
        {
            var x = left + i * (right - left) / (count - 1);
            var y = bottom - (v - min) / range * (bottom - top);
            return new Point(x, y);
        }

        // 步骤 6：构造面积几何。
        // 面积
        // 闭合路径 = 左下角 -> 沿曲线 -> 右下角，填充时才不会漏底。
        //
        // 四个动作逐个看：
        //   BeginFigure(new Point(Map(0,p[0],n).X, bottom), true)
        //     起点不是首点本身，而是「首点正下方、绘图区底边上的那个点」= (22, 146)；
        //     第二个参数 true 表示这条 figure 参与填充（false 只描边）。
        //   for 循环从 i=0 开始（注意与折线不同，折线从 1 开始），
        //     因为首点还没画过，必须补一次 LineTo 才能连上去。
        //   LineTo(new Point(Map(n-1, p[^1], n).X, bottom))
        //     p[^1] 是 C# 8 的「倒数第一个元素」语法，等价于 p[p.Count-1]。
        //     终点是「末点正下方、底边上的点」= (388, 146)。
        //   EndFigure(true) 从 (388,146) 直着连回起点 (22,146)，补出底边。
        //
        // 于是轮廓是：底边左端 -> 首点 -> 中间各点 -> 末点 -> 底边右端 -> 沿底边回到左端。
        // 若不先落到底边、直接从首点开始，闭合线会斜着从末点连回首点，面积会被削掉一块。
        //
        // 另外这个面积的左右边界是 22 和 388 而不是 0 和 w，
        // 与网格线（0..w）不一致：网格会从面积两侧各露出 22 / 12 像素，视觉上像是「面积浮在网格上」。
        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(Map(0, p[0], p.Count).X, bottom), true);
            for (var i = 0; i < p.Count; i++) ctx.LineTo(Map(i, p[i], p.Count));
            ctx.LineTo(new Point(Map(p.Count - 1, p[^1], p.Count).X, bottom));
            ctx.EndFigure(true);
        }
        // 取主曲线的纯色作为面积基色；若传入的是渐变/图片画刷则退回主色。
        // 注意这个判断是「兜底」而不是「取平均色」：渐变画刷做不出同色系半透明版本，
        // 只能退化成品牌主色，这时面积颜色会与线条颜色对不上。
        var color = primary is ISolidColorBrush scb ? scb.Color : Color.Parse("#6C8BFF");
        // alpha 82/255 ≈ 0.32：比 Sparkline 的 0.38 稍淡，因为大图面积更大，太浓会压住网格。
        // 两个 stop 直接写绝对 alpha（82 -> 0），而不像 Sparkline 那样走 0..1 的 WithAlpha，
        // 是因为这里已经有 color 了，没必要再多一层函数调用。
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
        // 步骤 7：把数据点先算成 Point 列表再统一绘制。
        // 先算成列表是为了复用：下面的折线和数据点循环都用同一批坐标，
        // 若两处各算一次 Map，浮点结果虽然相同但要多跑一遍循环。
        var linePts = new List<Point>();
        for (var i = 0; i < p.Count; i++) linePts.Add(Map(i, p[i], p.Count));
        DrawPolyline(context, linePts, new Pen(primary, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round));

        // 数据点：先填深底 #13161D 再描边，形成「挖空」效果，让点能从面积渐变上跳出来；
        // 半径 3 与 2px 描边配合，视觉直径约 8px，正好是设计稿的圆点大小。
        //
        // 为什么必须是「深色填充 + 彩色描边」而不是直接画一个实心彩点：
        //   1) 遮挡线段：这个圆画在折线之后（z 序更高），圆心又正好落在折线上，
        //      深色填充把穿过圆心的那段线盖掉，节点就从「线上的一个点」变成「线上的一个环」；
        //   2) 制造节点感：环形比实心圆多一圈亮边，在同样 8px 直径下更醒目，
        //      远处的实心点会糊成一个色块，环形的中空反而保住了形状；
        //   3) 与面积渐变解耦：填充色是页面底色 #13161D，所以无论下面的渐变多浓，
        //      圆内始终是「干净」的，不会被面积色污染。
        //   尺寸换算：椭圆半径 3，Pen 宽 2 以轮廓为中心向两侧各扩 1，
        //   外沿半径 = 3 + 1 = 4，直径 8 —— 这就是注释里「视觉直径约 8px」的算法来源。
        //   每个点都 new 一支 Pen 和一个画刷：点数在几十的量级，这点分配可以忽略。
        foreach (var pt in linePts)
        {
            context.DrawEllipse(new SolidColorBrush(Color.Parse("#13161D")), new Pen(primary, 2), pt, 3, 3);
        }

        // 次曲线（虚线）
        // 步骤 8：次曲线最后画，因此它压在主曲线和数据点之上（z 序最高）。
        // 如果次曲线穿过了主曲线的某个数据点，虚线会盖在圆环上面 —— 这是绘制顺序的直接后果，
        // 当前数据下两条线基本不交叉，看不出问题。
        // DashStyle(4,4) 表示 4px 实线 + 4px 空白；虚线不面积填充，避免两条半透明面积互相叠加变脏。
        //
        // 关于 DashStyle 的构造参数，需要说得更准确一些：
        //   new DashStyle(new double[] { 4, 4 }, 0)
        //     第一个参数 dashes：实线长度与空白长度交替的序列，这里 4 和 4 表示等长交替；
        //     第二个参数 offset：相位偏移量，表示「从虚线周期里的第几个单位开始画」。
        //       offset = 0 时从实线段的起点开始，所以第一个点是实线；
        //       若写 offset = 2，起笔位置就前进了 2，开头 2 个单位仍是实线，接着才进入空白，
        //       整条虚线的「斑马纹」会整体挪一段距离 —— 常用来让相邻两条虚线错开，避免看起来像一条粗线。
        //   数值单位：Avalonia 与 WPF 一致，dashes 与 offset 的单位是「线宽的倍数」而不是绝对像素。
        //   所以 4 在 1.8 的线宽下约为 4*1.8 = 7.2px 实线 + 7.2px 空白，
        //   而不是上一行注释里写的 4px。想要接近 4px 的视觉节奏，应该传 4/1.8 ≈ 2.2。
        //   好在圆头线帽（Round）会让每段虚线两端各鼓出 0.9px，实际看起来比 7.2 更连贯。
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
        // 前置状态：Values 可能为 null / 空 / 全 0 / 含负数；Colors 可能为 null（走内置调色板）。
        // 后置状态：不写字段；有两条提前 return——
        //   1) 尺寸过小：连轨道都不画；
        //   2) Values 为空或总和 <= 0：只留一条半透明轨道当占位。
        //   「只留轨道」这个设计的好处是空数据时布局不塌，用户能看出这里本该有个环。
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 8 || h <= 8) return;

        // 步骤 1：算半径与圆心。
        // 取 min(w,h) 保证圆环在任何非方形区域里都不被拉成椭圆。
        var size = Math.Min(w, h);
        var thickness = RingThickness;
        // 半径取「外沿 - 半个环宽」，因为描边以路径为中心向两侧各扩展 thickness/2。
        //
        // 完整推导（记外沿半径 R，路径半径 r，环宽 t）：
        //   描边以路径为中心，向内外各扩 t/2，所以：
        //     外沿 = r + t/2
        //     内沿 = r - t/2
        //   要求外沿正好贴住控件边界，即 R = size/2：
        //     r + t/2 = size/2  ->  r = size/2 - t/2
        //   同时可得内孔半径 = r - t/2 = size/2 - t。
        // 算例：size = 100、t = 12（默认值）
        //     r = 50 - 6 = 44
        //     外沿 = 44 + 6 = 50（正好贴边）
        //     内孔半径 = 44 - 6 = 38，内孔直径 76
        //   若错误地令 r = size/2 = 50，外沿会到 56，比控件多出 6px，圆环四周被裁平。
        var radius = size / 2 - thickness / 2;
        // 圆心取控件中心：非方形时圆环居中，两侧（或上下）留空。
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
        // 每次 Render 都新建这个数组：4 个 Color 结构体的开销可以忽略，
        // 换成静态字段反而要处理「外部改不了它」的防御性拷贝问题。
        var palette = new[]
        {
            Color.Parse("#6C8BFF"), Color.Parse("#A78BFA"), Color.Parse("#3DD6E8"), Color.Parse("#3DDC97")
        };

        // 步骤 2：逐段累加角度画弧。
        // 从 -90° 起画：屏幕坐标 0° 指向 3 点方向，-90° 才是 12 点方向，视觉上从正上方开始顺时针排布。
        // 为什么 0° 是 3 点：下面 ArcGeometry 用 (cos θ, sin θ) * r 求点，
        //   θ=0   -> (r, 0)  -> 圆心右侧，即 3 点；
        //   θ=90  -> (0, r)  -> 圆心下方，即 6 点（因为屏幕 y 向下）；
        //   θ=-90 -> (0, -r) -> 圆心上方，即 12 点。
        // 所以「-90 起、角度递增」在屏幕上就是「12 点起、顺时针」。
        // 这是环形图、饼图的行业惯例：从 12 点开始，最重要的第一段占据右上象限。
        var start = -90.0;
        for (var i = 0; i < values.Count; i++)
        {
            // 百分比 -> 角度：v/total 是占比（0..1），乘 360 换成角度。
            // 所有段的 sweep 之和恒等于 360（前提是没有被下面的 continue 跳过）。
            var sweep = values[i] / total * 360.0;
            // 占比极小的段直接跳过：既看不见，又会因为只剩 1.5° 间隙而画成一个小点。
            // 注意 continue 会连带跳过下面的 start += sweep，被跳过的角度不会补回，
            // 后续分段的起点会略微前移；对整体观感没有影响，但严格来说首尾不会精确闭合。
            //
            // 把「副作用」写清楚：假设第 k 段被跳过、它的 sweep 是 0.05°，
            // 那么第 k+1 段仍然从 start 开始画（而不是 start+0.05），
            // 于是它整体比「理论位置」早了 0.05°，后面所有段一起前移 0.05°。
            // 如果累计跳过了 S 度，最后一段画完时角度只走到 360-S，
            // 于是 12 点方向（起点）那个缺口会从 1.5° 变宽成 1.5°+S。
            // 跳过的段越多、缺口越宽，这就是注释里「首尾不精确闭合」的实际表现。
            if (sweep <= 0.1) continue;
            // 取色：优先用外部传入的 Colors（要求长度够），否则按序号在调色板里取模循环。
            // 取模意味着第 5 段会重新用主色，颜色开始重复 —— 段数超过 4 时请务必传 Colors。
            var color = colors is not null && i < colors.Count ? colors[i] : palette[i % palette.Length];
            // 每段末尾少画 1.5°，形成分段之间的空隙；间隙从本段的角度里扣，因此后面的累计起点仍按完整 sweep 前进。
            // 占比刚过 0.1° 的段扣除间隙后 sweep 可能为负，会画出一小段反向弧；真实数据下极少出现。
            //
            // 关于这 1.5° 的间隙，有一个与直觉不符的实际效果：
            //   下面这支 Pen 用了 PenLineCap.Round，圆头线帽会在弧的两端各多画一个半径 thickness/2 的半圆。
            //   默认 thickness = 12 时，单端就伸出 6px；而 1.5° 在 r=44 的弧长只有
            //     44 * 1.5 * π/180 ≈ 1.15px。
            //   两端各伸 6px、中间只有 1.15px 的空隙，圆头必然互相重叠，
            //   结果是「设了 1.5° 间隙，屏幕上却几乎看不到缝」，两段颜色直接相接、还可能轻微叠色。
            //   想要真正看到缝隙，需要把间隙加大到 thickness 对应的角度（约 15.6°）以上，
            //   或者把线帽改成 PenLineCap.Flat / Butt。
            var geo = ArcGeometry(center, radius, start, sweep - 1.5);
            context.DrawGeometry(null, new Pen(new SolidColorBrush(color), thickness, lineCap: PenLineCap.Round), geo);
            // 前进整段角度（不是 sweep-1.5）：间隙是从「本段」里扣的，不是从「间距」里挤的。
            // 这样第 i+1 段与第 i 段的起点差始终是理论角度，累积误差不会逐段放大。
            start += sweep;
        }
    }

    /// <summary>
    /// 把「起始角 + 扫过角」换算成一段圆弧几何（角度制，屏幕坐标系顺时针为正）。
    /// </summary>
    private static Geometry ArcGeometry(Point center, double radius, double startAngle, double sweepAngle)
    {
        // 角度转弧度：三角函数需要弧度，且 y 轴向下，正角自然表现为顺时针。
        // 换算公式：弧度 = 角度 * π / 180。-90° -> -1.5708 rad。
        var a0 = startAngle * Math.PI / 180;
        // 终点角 = 起点角 + 扫过角。注意这里没有取模：sweep 为负时 a1 会小于 a0，
        // 于是 p1 落在「反方向」，ArcTo 会配合下面的 Clockwise 画出一段反向大弧（见末尾说明）。
        var a1 = (startAngle + sweepAngle) * Math.PI / 180;
        // 圆上取点：x = cx + r*cosθ，y = cy + r*sinθ。
        // 因为屏幕 y 向下，sin 的正方向是「往下」，所以角度增大 = 屏幕上顺时针。
        var p0 = new Point(center.X + radius * Math.Cos(a0), center.Y + radius * Math.Sin(a0));
        var p1 = new Point(center.X + radius * Math.Cos(a1), center.Y + radius * Math.Sin(a1));

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(p0, false);
            // ArcTo 参数：终点、椭圆半径、旋转角、largeArc、sweepDirection。
            // largeArc 必须按扫过角是否大于 180° 决定，否则超过半圈时 SVG 会选「短的那条弧」，画出的段会反向或缺失；
            // Clockwise 与角度增大的方向一致（屏幕 y 向下），这样圆弧才顺着累计角度延伸。
            //
            // 五个参数逐位对应：
            //   1) p1              弧的终点（起点是 BeginFigure 给的 p0）
            //   2) new Size(r, r)  椭圆的两个半轴；相等就是圆弧
            //   3) 0               椭圆相对坐标系的旋转角，圆用不上，恒为 0
            //   4) largeArc        在「过 p0、p1 的两条候选弧」里选长的那条（true）还是短的那条（false）。
            //                      这个标志只在两点之间的圆心角可能超过 180° 时才有意义；
            //                      判断依据就是 sweepAngle > 180：一段 240° 的扇形必须走大弧，
            //                      否则渲染器会挑那条 120° 的小弧，颜色段会「缺一块」甚至跑到对面去。
            //                      边界上的 180° 两弧等长，传 true / false 结果一样。
            //   5) Clockwise       沿角度增大方向画（屏幕顺时针）。与上面 start 累加的方向必须一致，
            //                      否则每段弧都会朝着反方向绕远路，整圈看起来像被「切碎并翻转」。
            ctx.ArcTo(p1, new Size(radius, radius), 0, sweepAngle > 180,
                SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        return geo;
    }
}
