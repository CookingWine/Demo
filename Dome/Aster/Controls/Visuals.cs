// 文件：Visuals.cs
// 作用：一组「有画面感」的小型自绘控件——品牌 Logo、音频柱、黑胶唱片、可拖动进度条、播放跳动指示器。
// 使用方：侧边栏、播放器页与顶栏，XAML 中通过 <ctrl:LogoMark/>、<ctrl:AudioBars/> 等直接放置。
// 实现：全部继承 Control 并重写 Render；动画类控件用 DispatcherTimer 推进时间变量后 InvalidateVisual。
// 性能：计时器只在控件挂到可视树时启动、移除时停止，且 Tick 内再判 IsVisible，
//       避免页面切走后仍在后台空转重绘（自绘控件没有 Composition 动画，重绘是实打实的 CPU 开销）。
// 精度：所有几何按 Bounds 实时计算，尺寸变化无需额外监听；渐变使用相对坐标，缩放时自然跟随。
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Aster.Controls;

/// <summary>Aster 五瓣花标识（设计稿 rotate 0/72/144/216/288）。</summary>
// 画法：同一片椭圆花瓣绕中心旋转 5 次，再叠一个花心与高光点，不需要任何图片资源。
// 尺寸：以 64 为设计基准（k = min(w,h)/64），因此 20px 的角标和 96px 的启动页共用同一套坐标。
public class LogoMark : Control
{
    /// <summary>花瓣填充色，默认 null 时回退到深底 #0A0B12（挖空式 Logo）。</summary>
    public static readonly StyledProperty<IBrush?> PetalBrushProperty =
        AvaloniaProperty.Register<LogoMark, IBrush?>(nameof(PetalBrush));

    /// <summary>花心高光色，默认 null 时回退为白色 55% 透明度。</summary>
    public static readonly StyledProperty<IBrush?> CoreBrushProperty =
        AvaloniaProperty.Register<LogoMark, IBrush?>(nameof(CoreBrush));

    // 两种颜色都直接参与绘制，任一变化都需要重绘。
    static LogoMark()
    {
        AffectsRender<LogoMark>(PetalBrushProperty, CoreBrushProperty);
    }

    /// <summary>花瓣颜色，默认 #0A0B12。</summary>
    public IBrush? PetalBrush
    {
        get => GetValue(PetalBrushProperty);
        set => SetValue(PetalBrushProperty, value);
    }

    /// <summary>花心高光颜色，默认白色 55%。</summary>
    public IBrush? CoreBrush
    {
        get => GetValue(CoreBrushProperty);
        set => SetValue(CoreBrushProperty, value);
    }

    /// <summary>
    /// 绘制入口：循环 5 次旋转绘制花瓣，最后在屏幕坐标里补花心与高光。
    /// </summary>
    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var brush = PetalBrush ?? new SolidColorBrush(Color.Parse("#0A0B12"));
        var core = CoreBrush ?? new SolidColorBrush(Colors.White, 0.55);
        // 64 是设计稿的画布边长；取 min(w,h) 保证 Logo 在非方形控件中也不会变形。
        var k = Math.Min(w, h) / 64.0;

        // 设计稿为 5 瓣（rotate 0/72/144/216/288），不是 6 瓣
        for (var i = 0; i < 5; i++)
        {
            // 花瓣坐标已相对中心 (0,-11)，只需 缩放 → 旋转 → 平移
            // 矩阵顺序不能颠倒：Avalonia 是行向量左乘（v * M1 * M2），越靠左越先作用。
            // 先缩放：把 64 基准的花瓣尺寸换算成当前像素；再旋转：绕局部原点 (0,0) 转 72°；
            // 最后平移到控件中心。若先平移再旋转，花瓣会绕着控件左上角公转而不是绕自身中心开花。
            var m = Matrix.CreateScale(k, k)
                    * Matrix.CreateRotation(i * 2 * Math.PI / 5)
                    * Matrix.CreateTranslation(w / 2, h / 2);
            // PushTransform 返回 IDisposable：出 using 时弹出该层变换，5 次循环互不干扰。
            using (context.PushTransform(m))
            {
                // 局部坐标 (0,-10.6) 是花瓣中心（即原注释说的 (0,-11) 量级），
                // 半径 7.4 x 11.0 让花瓣呈纵向椭圆，旋转后自然形成花瓣交叠的花形。
                context.DrawEllipse(brush, null, new Point(0, -10.6), 7.4, 11.0);
            }
        }

        // 花心与高光直接画在屏幕坐标系（乘 k 换算），不再套用上面的旋转矩阵：
        // 它们的圆心就是旋转中心，转与不转视觉完全一致，单独绘制可以少两次 Push/Pop。
        context.DrawEllipse(brush, null, new Point(w / 2, h / 2), 6.2 * k, 6.2 * k);
        // 2.5 的小白点作为高光，让纯色 Logo 在深底上仍有层次。
        context.DrawEllipse(core, null, new Point(w / 2, h / 2), 2.5 * k, 2.5 * k);
    }
}

/// <summary>音频可视化柱状图（带轻微动画）。</summary>
// 说明：柱高并不来自真实音频频谱，而是「固定基础高度 + 正弦摆动」合成的装饰性动画。
// 目的是给播放页一个持续变化的视觉焦点，因此不引入 FFT 或音频回调。
public class AudioBars : Control
{
    // 18 根柱子的参数在构造时一次算好，Render 只做查表 + 三角函数，避免每帧 new/随机。
    private readonly DispatcherTimer _timer;
    private readonly double[] _levels;
    private readonly double[] _speed;
    private double _t;

    public AudioBars()
    {
        const int count = 18;
        _levels = new double[count];
        _speed = new double[count];
        // 固定种子 7：每次启动的柱形与摆动节奏完全一致，方便对照设计稿，
        // 也让「随机感」保持稳定，不会出现某根柱子永远特别高的情况。
        var rnd = new Random(7);
        for (var i = 0; i < count; i++)
        {
            // _levels 决定基础高度（0.25~0.85），_speed 决定正弦摆动频率（0.8~2.2），两者错开才不整齐划一。
            _levels[i] = 0.25 + rnd.NextDouble() * 0.6;
            _speed[i] = 0.8 + rnd.NextDouble() * 1.4;
        }

        // 45ms 约等于 22fps：柱状图本身节奏慢，没必要跑 60fps，低帧率可显著降低常驻 CPU。
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _timer.Tick += (_, _) =>
        {
            // _t 是全局相位，每帧固定增量，保证动画速度与帧率无关（掉帧时只是跳步，不会变慢）。
            _t += 0.09;
            // 不可见时不请求重绘：控件可能仍在可视树中但被折叠/切到后台标签页。
            if (IsVisible) InvalidateVisual();
        };
    }

    /// <summary>是否处于播放状态，默认 true；为 false 时柱子收敛为静态低矮形态。</summary>
    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<AudioBars, bool>(nameof(IsPlaying), true);

    /// <summary>是否正在播放，默认 true。</summary>
    public bool IsPlaying
    {
        get => GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    /// <summary>
    /// 挂到可视树时启动计时器：只有真正显示出来才需要动画，构造期启动会浪费整个应用生命周期。
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    /// <summary>
    /// 从可视树移除时停止计时器，防止已销毁页面继续触发 Tick（DispatcherTimer 会持有控件引用，还可能阻止 GC）。
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    /// <summary>
    /// 绘制入口：把 18 根柱子在水平方向居中排布，柱高由正弦波调制后从底部向上生长。
    /// </summary>
    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 4 || h <= 4) return;

        // 柱宽 5 + 间距 4 是设计稿给定的固定值，总宽先算出来才能做水平居中。
        // 18 根柱子合计 158px：控件更窄时 startX 会变成负数，柱子会被控件边界裁掉（不会抛异常）。
        const double barW = 5;
        const double gap = 4;
        var count = _levels.Length;
        var totalW = count * barW + (count - 1) * gap;
        var startX = (w - totalW) / 2;

        // 竖向三色渐变（紫 -> 主色 -> 青），相对坐标意味着每根柱子各自从顶到底渐变，
        // 而不是整块区域共用一条渐变——这样矮柱子也保持完整的色彩过渡。
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
            // 每根柱子加上 i*0.7 的相位偏移，邻居之间不会同步起伏，视觉上更像真实频谱。
            // sin 的值域 [-1,1] 经 *0.5+0.5 映射到 [0,1]，再乘基础高度得到当前振幅。
            var wave = Math.Sin(_t * _speed[i] + i * 0.7) * 0.5 + 0.5;
            // 播放时保留 0.12 的底高并让波形主导；暂停时压到 0.12 + 基础高度的 10%，形成「安静下来」的对比。
            var level = IsPlaying ? 0.12 + _levels[i] * wave * 0.9 : 0.12 + _levels[i] * 0.1;
            // 下限 0.08 防止振幅过零时柱子彻底消失，看起来像闪烁。
            level = Math.Clamp(level, 0.08, 1);

            // 最大高度留出上下各 4px，圆角矩形的两端才不会被 Bounds 裁掉；Math.Max(4,...) 保证最小可见高度。
            var barH = Math.Max(4, (h - 8) * level);
            var x = startX + i * (barW + gap);
            // y 用 h - 4 - barH：所有柱子共享同一条「地平线」，向上生长而不是从顶部垂下。
            var rect = new Rect(x, h - 4 - barH, barW, barH);
            // 圆角半径 2.5 = 柱宽的一半，柱顶自然形成半圆帽。
            context.DrawRectangle(brush, null, rect, 2.5, 2.5);
        }
    }
}

/// <summary>旋转的黑胶唱片。</summary>
// 用三层同心图形模拟唱片：径向渐变盘体 + 白色 4.5% 的纹路圆环 + 紫青渐变的中心标签。
// 每帧只改变换角度，不重建几何数据，因此 20fps 下开销很低。
public class SpinningDisc : Control
{
    // 计时器负责推进动画；_angle 用角度制保存，取模 360 后不会随时间无限增大而丢失浮点精度。
    private readonly DispatcherTimer _timer;
    private double _angle;

    public SpinningDisc()
    {
        // 50ms = 20fps，配合每帧 1.4° 约等于 28°/秒，转一圈约 12.9 秒；
        // 真实黑胶 33⅓ 转约 1.8 秒一圈，这里刻意放慢，作为装饰性动画更耐看也更省 CPU。
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) =>
        {
            // 取模避免长时间运行后角度值过大、三角函数精度下降。
            _angle = (_angle + 1.4) % 360;
            if (IsVisible) InvalidateVisual();
        };
    }

    /// <summary>是否旋转，默认 true。</summary>
    public static readonly StyledProperty<bool> IsSpinningProperty =
        AvaloniaProperty.Register<SpinningDisc, bool>(nameof(IsSpinning), true);

    /// <summary>是否旋转；当前仅作为对外状态标记，计时器本身不受它控制。</summary>
    public bool IsSpinning
    {
        get => GetValue(IsSpinningProperty);
        set => SetValue(IsSpinningProperty, value);
    }

    /// <summary>
    /// 挂到可视树时启动计时器，避免控件未显示时也持续 Tick。
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    /// <summary>
    /// 移除时停止计时器，否则动画会一直跑在后台。
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    /// <summary>
    /// 绘制入口：整体绕圆心旋转后，依次画盘体、纹路、中心标签。
    /// </summary>
    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 4 || h <= 4) return;

        var center = new Point(w / 2, h / 2);
        var r = Math.Min(w, h) / 2;

        // CreateRotation(angle, center) 是绕指定点旋转（内部等价于 平移到原点 -> 旋转 -> 平移回去），
        // 这样唱片自转而不是绕控件左上角公转；using 保证离开作用域后恢复原坐标系。
        using (context.PushTransform(Matrix.CreateRotation(_angle * Math.PI / 180, center)))
        {
            // 盘体
            // 径向渐变：GradientOrigin 偏到左上 (0.38,0.32)，模拟单一光源从左上打过来的高光；
            // 四个 stop 依次是 高光灰 -> 主盘面 -> 边缘反光 -> 最外圈暗边，制造厚度感。
            var body = new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.38, 0.32, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.55, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.55, RelativeUnit.Relative),
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
            // 4 圈等距细环，半径从 0.65r 到 0.95r（0.55 + i*0.1）；透明度只有 4.5%，
            // 足够在深色盘面上看出「沟槽」，又不会抢走中心标签的注意力。
            var groove = new Pen(new SolidColorBrush(Colors.White, 0.045), 1);
            for (var i = 1; i <= 4; i++)
            {
                var rr = r * (0.55 + i * 0.1);
                context.DrawEllipse(null, groove, center, rr, rr);
            }

            // 中心标签
            // 45° 对角渐变（紫 -> 青）是品牌色，半径 0.245r 大致对应真实唱片标签的比例。
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
            // 中心轴孔：0.035r，用最深底色画出「洞」，让标签不至于显得是一张贴纸。
            context.DrawEllipse(new SolidColorBrush(Color.Parse("#0E0F18")), null, center, r * 0.035, r * 0.035);
        }
    }
}

/// <summary>进度/音量条（可拖动，悬停显示滑块）。</summary>
// 交互模型：按下即定位 + 捕获指针，移动时持续换算，抬起释放捕获；不做惯性、不做键盘调节。
// 约束：拖动写入的 Value 始终钳制在 [Minimum, Maximum]；绘制时也按该区间换算比例，外部赋超界值只会顶到两端。
public class TrackBar : Control
{
    // 拖动状态与 IsPointerOver 的区别：IsPointerOver 只看悬停，_dragging 保证指针移出控件后仍继续跟随。
    private bool _dragging;

    // defaultBindingMode: TwoWay 让控件内部 SetValue 能自动回写到 ViewModel：
    // 拖动时这里只写 Value，绑定的播放进度/音量属性会同步更新；
    // 若用默认的 OneWay，拖动产生的值只存在于控件里，业务层收不到，进度条就「拖了个寂寞」。
    /// <summary>当前值，默认 0，双向绑定（拖动时自动回写数据源）。</summary>
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<TrackBar, double>(nameof(Value), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>最大值，默认 100。</summary>
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<TrackBar, double>(nameof(Maximum), 100d);

    /// <summary>最小值，默认 0。</summary>
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<TrackBar, double>(nameof(Minimum), 0d);

    /// <summary>已填充部分的画刷，默认 null 时使用主色到紫色的横向渐变。</summary>
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<TrackBar, IBrush?>(nameof(Fill));

    /// <summary>轨道高度，默认 4；圆角半径跟随该值取一半。</summary>
    public static readonly StyledProperty<double> TrackHeightProperty =
        AvaloniaProperty.Register<TrackBar, double>(nameof(TrackHeight), 4d);

    // 拖动过程中 Value 持续变化，靠 AffectsRender 自动重绘，无需手动调用 InvalidateVisual。
    static TrackBar()
    {
        AffectsRender<TrackBar>(ValueProperty, MaximumProperty, MinimumProperty, FillProperty, TrackHeightProperty);
    }

    public TrackBar()
    {
        // 不参与焦点遍历：这类条状控件若可聚焦，Tab 键会在每个进度条上停留一次，体验很差。
        Focusable = false;
        // 手型光标提前告知「这里可以拖」，比悬停时才变光标更明确。
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>当前值，默认 0，双向绑定。</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>最大值，默认 100。</summary>
    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>最小值，默认 0。</summary>
    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>填充色，默认 null 时使用 #6C8BFF -> #A78BFA 渐变。</summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>轨道高度，默认 4。</summary>
    public double TrackHeight
    {
        get => GetValue(TrackHeightProperty);
        set => SetValue(TrackHeightProperty, value);
    }

    /// <summary>
    /// 按下时立刻跳转到点击位置，并捕获指针——这是「可拖动」的关键。
    /// </summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _dragging = true;
        // Capture 让后续 Moved/Released 事件即使指针移出控件边界也继续送给本控件，
        // 否则用户快速横向拖动会中途丢失跟踪、滑块「粘」在原地。
        e.Pointer.Capture(this);
        // 先定位再重绘：支持「点哪跳哪」，而不是只能从当前滑块位置开始拖。
        SetFromPoint(e.GetPosition(this).X);
        InvalidateVisual();
        // 标记已处理，避免外层（如播放器整块点击区域）再响应同一次按下。
        e.Handled = true;
    }

    /// <summary>
    /// 按住移动时持续更新值；未按下时不做任何事（悬停移动由 IsPointerOver 控制滑块显隐）。
    /// </summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) SetFromPoint(e.GetPosition(this).X);
    }

    /// <summary>
    /// 抬起时结束拖动并释放捕获，同时重绘以隐藏滑块（若指针已移出）。
    /// </summary>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    /// <summary>
    /// 指针进入：重绘以显示滑块。IsPointerOver 是 Control 自带属性，这里只负责触发刷新。
    /// </summary>
    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        InvalidateVisual();
    }

    /// <summary>
    /// 指针离开：重绘以隐藏滑块；若仍在拖动（指针被捕获），条件里会保留显示。
    /// </summary>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        InvalidateVisual();
    }

    /// <summary>
    /// 把控件内的 X 坐标换算成 Value：先归一化到 0..1 再映射到 [Minimum, Maximum]，
    /// Clamp 保证拖到边界外时不会产生超界值。
    /// </summary>
    private void SetFromPoint(double x)
    {
        var w = Bounds.Width;
        // 布局尚未完成时宽度可能为 0，直接返回避免除零得到 NaN。
        if (w <= 0) return;
        var ratio = Math.Clamp(x / w, 0, 1);
        Value = Minimum + ratio * (Maximum - Minimum);
    }

    /// <summary>
    /// 绘制入口：底轨 -> 已填充部分 -> 悬停/拖动时的白色圆点。
    /// </summary>
    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 2) return;

        var th = TrackHeight;
        // 轨道垂直居中：控件高度通常由外部给（如 14），比轨道高，留白用于放置滑块。
        var cy = h / 2;
        var trackRect = new Rect(0, cy - th / 2, w, th);
        // 圆角半径取 th/2，轨道两端正好是半圆。
        context.DrawRectangle(new SolidColorBrush(Colors.White, 0.10), null, trackRect, th / 2, th / 2);

        var span = Maximum - Minimum;
        // span 兜底为 1：Max == Min 的错误配置下避免除零，此时进度固定为 0。
        if (span <= 0) span = 1;
        var ratio = Math.Clamp((Value - Minimum) / span, 0, 1);
        var fillW = w * ratio;
        // 宽度小于 0.5 时不画，避免开头出现一个几乎不可见的圆角毛刺。
        if (fillW > 0.5)
        {
            // 默认填充是「主色 -> 紫」的横向渐变，与播放器整体的霓虹风格一致。
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

        // 滑块只在悬停或拖动时出现：静止时保持「一条干净的线」，符合设计稿的极简要求。
        if (IsPointerOver || _dragging)
        {
            // 钳制到 [5, w-5]：保证半径为 5 的圆点在 0% 和 100% 时仍完整可见，不会被控件边界裁掉一半。
            // 这里隐含要求控件宽度不小于 10，否则 Clamp 的下界会大于上界而抛 ArgumentException。
            var cx = Math.Clamp(fillW, 5, w - 5);
            context.DrawEllipse(new SolidColorBrush(Colors.White), null, new Point(cx, cy), 5, 5);
        }
    }
}


/// <summary>三格跳动指示器（正在播放）。</summary>
// 用于列表行里标记「当前正在播放」，尺寸通常只有 12~14px，因此只画 3 根 2px 宽的细柱。
// 动画只依赖一个时间变量 _t，三根柱子通过相位差错开，无需为每根柱子保存独立状态。
public class EqIndicator : Control
{
    private readonly DispatcherTimer _timer;
    private double _t;

    public EqIndicator()
    {
        // 120ms ≈ 8fps：指示器体积很小，8fps 已足够表现「跳动」，再高只会增加后台开销。
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _timer.Tick += (_, _) =>
        {
            // 0.35 的步长配合三根柱子 1.1 的相位差，形成不同步的上下起伏。
            _t += 0.35;
            if (IsVisible) InvalidateVisual();
        };
    }

    /// <summary>
    /// 挂到可视树时启动计时器：列表里可能同时存在多个指示器，未显示的就不该消耗资源。
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    /// <summary>
    /// 移除时停止计时器，避免滚动出视野的行仍在后台重绘。
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    /// <summary>
    /// 绘制入口：三等分宽度画 3 根底部对齐的竖柱，高度由正弦波驱动。
    /// </summary>
    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 2 || h <= 2) return;

        var brush = new SolidColorBrush(Color.Parse("#6C8BFF"));
        // 柱宽固定 2px，剩余宽度平均分成两段间隙，因此整体在控件内左中右均布。
        const double barW = 2;
        var gap = (w - barW * 3) / 2;

        for (var i = 0; i < 3; i++)
        {
            // sin 值域 [-1,1] 先映射到 [0,1]，再压到 0.35~1.0：柱子始终保留 35% 高度，不会完全消失。
            // i * 1.1 的相位差让三根柱子轮流到达波峰，看起来像均衡器在跳动。
            var level = 0.35 + 0.65 * (Math.Sin(_t + i * 1.1) * 0.5 + 0.5);
            var barH = Math.Max(2, h * level);
            // 底部对齐（y = h - barH），圆角 1 让 2px 宽的细柱两端略微软化。
            context.DrawRectangle(brush, null, new Rect(i * (barW + gap), h - barH, barW, barH), 1, 1);
        }
    }
}
