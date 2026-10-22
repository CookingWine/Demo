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
        // 前置状态：无字段依赖，PetalBrush / CoreBrush 两个属性有缺省回退色。
        // 后置状态：不写字段；每个花瓣压一次变换栈、画完立刻弹出，所以 5 次循环之间互不影响，
        //           方法退出后坐标系与进入时一致。
        // 触发者：AffectsRender 登记的两个颜色属性；尺寸变化由布局系统引起，也会重绘。
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        // 步骤 1：取两种颜色。缺省花瓣色 #0A0B12 与页面深底几乎同色，
        // 所以默认形态是「挖空式 Logo」：花瓣是背景色，只靠花心和高光勾出形状。
        var brush = PetalBrush ?? new SolidColorBrush(Color.Parse("#0A0B12"));
        var core = CoreBrush ?? new SolidColorBrush(Colors.White, 0.55);
        // 64 是设计稿的画布边长；取 min(w,h) 保证 Logo 在非方形控件中也不会变形。
        // k 是「设计稿 1 单位 = 屏幕多少像素」，20px 的角标得到 k=0.3125，96px 启动页得到 k=1.5。
        var k = Math.Min(w, h) / 64.0;

        // 步骤 2：五瓣循环。
        // 极坐标布局的本质：只画「一片朝上的花瓣」，然后用旋转把它复制到 5 个方位。
        // 每瓣的旋转角 = i * 72°，即 i * 2π/5 弧度：
        //   i=0 ->   0°（正上方）
        //   i=1 ->  72°
        //   i=2 -> 144°
        //   i=3 -> 216°
        //   i=4 -> 288°
        // 360/5 = 72，所以第 5 片（i=5，不画）会正好回到 0°，形成一个完美的五重旋转对称图形。
        // 这也是设计稿写成 rotate 0/72/144/216/288 的原因：五个角度把圆周等分。
        // 设计稿为 5 瓣（rotate 0/72/144/216/288），不是 6 瓣
        for (var i = 0; i < 5; i++)
        {
            // 花瓣坐标已相对中心 (0,-11)，只需 缩放 → 旋转 → 平移
            // 矩阵顺序不能颠倒：Avalonia 是行向量左乘（v * M1 * M2），越靠左越先作用。
            // 先缩放：把 64 基准的花瓣尺寸换算成当前像素；再旋转：绕局部原点 (0,0) 转 72°；
            // 最后平移到控件中心。若先平移再旋转，花瓣会绕着控件左上角公转而不是绕自身中心开花。
            //
            // 把「行向量右乘」这件事讲清楚：
            //   设点为行向量 p，矩阵乘法满足结合律，于是
            //     p * (S * R * T) = ((p * S) * R) * T
            //   括号的顺序就是作用顺序：先 S（缩放）、再 R（旋转）、最后 T（平移）。
            //   所以写在最左边的矩阵最先作用 —— 这和线性代数课上「列向量、矩阵写在左边」的习惯
            //   正好相反，是本类代码最容易写反的一处。
            //
            // 具体到一片花瓣（假设 w = h = 64，于是 k = 1，缩放这一步可以忽略）：
            //   局部点 (0,-10.6)（花瓣中心，位于原点正上方）
            //     --R(72°)--> (0*cos72 - (-10.6)*sin72, 0*sin72 + (-10.6)*cos72)
            //               = (10.6*0.9511, -10.6*0.3090)
            //               ≈ (10.08, -3.28)
            //     --T(32,32)--> (42.08, 28.72)
            //   第二片花瓣的中心就落在控件右上方，与第一片相距 2*10.6*sin36° ≈ 12.46。
            //   若顺序写成 T * R * S（先平移再旋转），旋转中心变成控件左上角 (0,0)，
            //   五片花瓣会绕着左上角公转成一圈，而不是在控件中心开花。
            var m = Matrix.CreateScale(k, k)
                    * Matrix.CreateRotation(i * 2 * Math.PI / 5)
                    * Matrix.CreateTranslation(w / 2, h / 2);
            // PushTransform 返回 IDisposable：出 using 时弹出该层变换，5 次循环互不干扰。
            using (context.PushTransform(m))
            {
                // 局部坐标 (0,-10.6) 是花瓣中心（即原注释说的 (0,-11) 量级），
                // 半径 7.4 x 11.0 让花瓣呈纵向椭圆，旋转后自然形成花瓣交叠的花形。
                //
                // 这组数怎么得到「花瓣」形状，拆成四步看（都在 64 单位的局部坐标系里）：
                //   1) 形状选椭圆而不是水滴：椭圆没有数学上的尖角，缩到 20px 时也不会出现锯齿，
                //      代价是「花瓣」两端都是圆的 —— 靠下面画的花心把内侧圆头盖掉，
                //      只留外侧那半个，看上去才像花瓣尖端。
                //   2) 长短轴比 7.4 : 11.0 ≈ 0.67：偏瘦长。轴比接近 1 会变成五个圆点，
                //      超过 0.8 则过于饱满、瓣与瓣分不开。
                //   3) 中心距原点 10.6：椭圆沿自身长轴覆盖 y ∈ [-21.6, +0.4]，
                //      内端几乎穿过花心（只差 0.4），外端到 21.6。
                //      21.6 / 32（画布半边长）= 67.5%，也就是 Logo 只占画布中心的七成，
                //      外面留出三分之一的空气 —— 这是设计稿给图标留的视觉呼吸区。
                //   4) 交叠：相邻两瓣中心距 = 2 * 10.6 * sin(36°) ≈ 12.46，
                //      而椭圆宽 14.8 > 12.46，所以左右各重叠 1.17 单位。
                //      重叠保证五瓣连成一体、不会出现「五个分开的橄榄」。
                //   最终可见的花瓣 = 椭圆被半径 6.2 的花心盖住内侧后剩下的部分：
                //      长约 21.6 - 6.2 = 15.4、宽 7.4 的一段弧形，这才是眼睛看到的「一瓣」。
                context.DrawEllipse(brush, null, new Point(0, -10.6), 7.4, 11.0);
            }
        }

        // 花心与高光直接画在屏幕坐标系（乘 k 换算），不再套用上面的旋转矩阵：
        // 它们的圆心就是旋转中心，转与不转视觉完全一致，单独绘制可以少两次 Push/Pop。
        //
        // 花心半径 6.2 的作用不只是「装饰」：它用花瓣色把五片椭圆靠近原点的交叠处整块盖住，
        // 于是看不出「五个椭圆」的内部接缝，只看到一整朵实心的花。
        // 覆盖范围可以算出来：每片花瓣沿长轴覆盖 y ∈ [-21.6, +0.4]，
        // 花心半径 6.2 会把其中 [-6.2, +0.4] 这一段（长约 6.6）盖掉，
        // 也就是每片花瓣靠内的三成被藏起来，剩下的 [-21.6, -6.2]（长 15.4）才是真正露出的「花瓣」。
        context.DrawEllipse(brush, null, new Point(w / 2, h / 2), 6.2 * k, 6.2 * k);
        // 2.5 的小白点作为高光，让纯色 Logo 在深底上仍有层次。
        // 2.5 / 6.2 ≈ 40%：正好是花心直径的四成，再大就像贴了个白点标签，再小则看不见。
        // 注意这里乘的是 k 而不是 64 基准下的绝对值：20px 角标上 k=0.3125，高光半径只有 0.78px，
        // 视觉上会退化成一点微光 —— 这是小尺寸下 Logo 该有的表现，不是 bug。
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
        // 精确值：1000 / 45 ≈ 22.2，即每秒 22 次 Tick。
        // DispatcherTimer 的几层含义需要说清：
        //   1) 它跑在 UI 线程的消息循环上，Tick 与布局、输入事件串行执行，
        //      所以 Tick 里改 _t 不会有线程安全问题，也不需要加锁；
        //   2) 它不是「精确闹钟」：如果 UI 线程正忙于一次长布局，Tick 会被推迟，
        //      这就是为什么 _t 用「每次加固定量」而不是「按真实时间差算」——
        //      掉帧时动画只是跳步，速度不会变慢，也不会因为一次卡顿突然跳很远；
        //   3) Interval 只是「最快多久一次」，实际帧率还受渲染管线合帧影响。
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _timer.Tick += (_, _) =>
        {
            // _t 是全局相位，每帧固定增量，保证动画速度与帧率无关（掉帧时只是跳步，不会变慢）。
            // 0.09 弧度/帧 × 22.2 帧/秒 ≈ 2 弧度/秒，一个完整正弦周期 2π 约 3.1 秒；
            // 再乘上 _speed 的 0.8~2.2，实际周期落在 1.4~3.9 秒之间，肉眼是「缓缓呼吸」的节奏。
            _t += 0.09;
            // 不可见时不请求重绘：控件可能仍在可视树中但被折叠/切到后台标签页。
            // InvalidateVisual 只是把一个脏标记挂到控件上，真正的 Render 在下一帧统一执行；
            // 所以这里多调用几次也不会画多次，渲染管线会把同一帧内的多次失效合并成一次。
            if (IsVisible) InvalidateVisual();
        };
        // 注意：计时器在构造函数里创建、绑定 Tick，但不在这里 Start。
        // 启动与停止分别放在 OnAttachedToVisualTree / OnDetachedFromVisualTree，
        // 这样页面没显示时不会有任何 Tick，控件被移出树后也能被 GC 回收
        // （DispatcherTimer 会持有 lambda，lambda 又捕获 this，不 Stop 就是一条泄漏链）。
    }

    /// <summary>是否处于播放状态，默认 true；为 false 时柱子收敛为静态低矮形态。</summary>
    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<AudioBars, bool>(nameof(IsPlaying), true);

    // 注意：IsPlayingProperty 没有登记进 AffectsRender（本类也没有静态构造）。
    // 所以切到「暂停」的那一刻并不会立即重绘，而是等下一次 Tick（≤45ms）才更新 ——
    // 因为计时器一直在跑，用户感觉不到延迟，所以这不是 bug，但依赖了「动画常驻」这个前提。
    // 如果将来为了省 CPU 把 Tick 改成只在播放时运行，那这里就必须补上 AffectsRender，
    // 否则暂停后画面会停在最后一帧的高柱子上。

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
        // 前置状态：_t 由计时器每 45ms 推进 0.09；_levels / _speed 在构造函数里一次性算好，此后只读。
        // 后置状态：不写任何字段，因此 Render 是幂等的 —— 同一 _t 重绘多少次结果都一样。
        // 触发者：计时器 Tick 里的 InvalidateVisual（这是本控件唯一的驱动源，没有任何属性变化会触发它）。
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 4 || h <= 4) return;

        // 步骤 1：算整组柱子的水平布局。
        // 柱宽 5 + 间距 4 是设计稿给定的固定值，总宽先算出来才能做水平居中。
        // 18 根柱子合计 158px：控件更窄时 startX 会变成负数，柱子会被控件边界裁掉（不会抛异常）。
        //
        // 算式拆开：18 根柱 × 5 = 90；柱子之间只有 17 个间隔 × 4 = 68；合计 158。
        //   为什么是 count-1 个间隔：两端不留缝，第一个柱子左边就是 startX，
        //   最后一个柱子右边就是 startX + totalW，这样居中才是真的居中。
        //   算例 w = 200：startX = (200-158)/2 = 21，
        //                 第 i 根柱的 x = 21 + i*9，i=0 -> 21，i=17 -> 174，
        //                 最后一根右边缘 = 174 + 5 = 179，右侧留白 = 200 - 179 = 21，与左侧对称。
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
            // 步骤 2：算这一根柱子的「响度」level。
            // 每根柱子加上 i*0.7 的相位偏移，邻居之间不会同步起伏，视觉上更像真实频谱。
            // sin 的值域 [-1,1] 经 *0.5+0.5 映射到 [0,1]，再乘基础高度得到当前振幅。
            //
            // 公式逐项拆解：sin(_t * _speed[i] + i * 0.7) * 0.5 + 0.5
            //   _t            全局相位，每 45ms 加 0.09，即约 2 弧度/秒。
            //   _speed[i]     每根柱子自己的频率倍数（0.8~2.2，构造时随机固定），
            //                 决定这根柱子「摆得快还是慢」，相邻柱子节奏不同才不会像波浪。
            //   i * 0.7       空间相位差，每根柱子之间错开 0.7 弧度（约 40°），
            //                 18 根累计错开 11.9 弧度（约 682°），所以整排柱子会形成
            //                 「几处波峰 + 几处波谷」的错落分布，而不是一个整齐的正弦波形。
            //   * 0.5 + 0.5   把 sin 的 [-1,1] 平移缩放到 [0,1]，得到纯正的调制系数 wave。
            var wave = Math.Sin(_t * _speed[i] + i * 0.7) * 0.5 + 0.5;
            // 播放时保留 0.12 的底高并让波形主导；暂停时压到 0.12 + 基础高度的 10%，形成「安静下来」的对比。
            //
            // 播放分支：0.12 + _levels[i] * wave * 0.9
            //   0.12 是「静音也保留的底高」，让柱子在波谷时不会缩成一条线；
            //   _levels[i] ∈ [0.25, 0.85] 是这根柱子的固有高度系数；
            //   wave ∈ [0,1] 是调制；0.9 是振幅上限，保证 0.12 + 0.9 = 1.02 不会明显超出画布。
            //   取值范围：wave=0 时 0.12，wave=1 且 _levels=0.85 时 0.12+0.765 = 0.885。
            // 暂停分支：0.12 + _levels[i] * 0.1 ∈ [0.145, 0.205]
            //   整体压到两成左右，柱子变成一排低矮的短桩，肉眼立刻能看出「没在播」。
            var level = IsPlaying ? 0.12 + _levels[i] * wave * 0.9 : 0.12 + _levels[i] * 0.1;
            // 下限 0.08 防止振幅过零时柱子彻底消失，看起来像闪烁。
            //
            // 需要如实说明：按当前常量，这个 Clamp 其实永远不生效 ——
            // 播放分支最低 0.12、最高 0.885，暂停分支 [0.145, 0.205]，全部落在 [0.08, 1] 内。
            // 它属于防御性代码：如果将来把 0.12 的底高调小、或把 0.9 的振幅放大到 1 以上，
            // 这里就是唯一兜底。改成 Math.Clamp 之前的老代码若没有它，参数一改就可能画到 Bounds 外面。
            level = Math.Clamp(level, 0.08, 1);

            // 步骤 3：把 level 换算成像素高度并落位。
            // 最大高度留出上下各 4px，圆角矩形的两端才不会被 Bounds 裁掉；Math.Max(4,...) 保证最小可见高度。
            //   (h - 8) 是可用高度：上下各让 4px 给圆角与抗锯齿；
            //   乘 level 得到实际高度；再用 Math.Max(4, ...) 托底。
            //   托底什么时候有用：(h-8)*0.12 < 4 即 h < 41.3 时才触发。
            //   常见高度（48~80）下最短的柱子也有 4.8~8.6px，所以这个 Max 在日常尺寸下不起作用。
            var barH = Math.Max(4, (h - 8) * level);
            var x = startX + i * (barW + gap);
            // y 用 h - 4 - barH：所有柱子共享同一条「地平线」，向上生长而不是从顶部垂下。
            //   h - 4 是地平线（距底 4px），再减 barH 得到顶边 y。
            //   因为高度按 level 变化，底边固定、顶边浮动，看起来就是「从地面往上跳」。
            var rect = new Rect(x, h - 4 - barH, barW, barH);
            // 圆角半径 2.5 = 柱宽的一半，柱顶自然形成半圆帽。
            // 注意半径也给到 2.5 的高度方向，所以矮到 5px 以下的柱子会退化成胶囊形。
            // 三个 stop 的渐变坐标是 RelativeUnit.Relative，参照物是「这根矩形自己的包围盒」，
            // 所以每根柱子都是完整的紫->蓝->青过渡，矮柱子不会被截断成只剩紫色。
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

    // 计时器同样只在挂树时启动、离树时停止（见下面两个重写方法），构造里只负责创建与绑定。
    // 与 AudioBars 的区别只是周期：唱片转得慢，20fps 就够；柱状图要跟随节奏，所以用 22fps。
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
        // 前置状态：_angle 由计时器每 50ms 增加 1.4 并取模 360。
        // 后置状态：不写字段；整个绘制包在一个 using 里，退出时旋转矩阵自动弹栈。
        // 触发者：计时器 Tick 里的 InvalidateVisual。
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 4 || h <= 4) return;

        // 圆心取控件中心；半径取短边的一半，所以唱片总是内切于控件的短边方向。
        // 非方形控件里会左右（或上下）多出空白，但唱片本身永远是正圆。
        var center = new Point(w / 2, h / 2);
        var r = Math.Min(w, h) / 2;

        // CreateRotation(angle, center) 是绕指定点旋转（内部等价于 平移到原点 -> 旋转 -> 平移回去），
        // 这样唱片自转而不是绕控件左上角公转；using 保证离开作用域后恢复原坐标系。
        //
        // 参数拆解：
        //   _angle * Math.PI / 180  角度制 -> 弧度制（Matrix 只认弧度）；
        //   center                  旋转中心，必须与下面绘制用的 center 是同一个坐标空间（控件本地坐标）；
        //                           若传 new Point(0,0)，整张唱片会像钟摆一样绕左上角甩出去。
        // 关于「旋转谁」：这个变换会把 using 块内的所有绘制一起转，包括渐变。
        // 也就是说光源也跟着转 —— 盘面高光会随着唱片一起绕圈，而不是固定从左上打光。
        // 真实黑胶在灯下转动时高光是钉住不动的，只是纹路在动。
        // 现在的做法省了一次变换，代价是仔细看能发现「高光在转」；若要去掉，
        // 需要把盘体渐变画在 PushTransform 之外，只让纹路和标签参与旋转。
        using (context.PushTransform(Matrix.CreateRotation(_angle * Math.PI / 180, center)))
        {
            // 盘体
            // 径向渐变：GradientOrigin 偏到左上 (0.38,0.32)，模拟单一光源从左上打过来的高光；
            // 四个 stop 依次是 高光灰 -> 主盘面 -> 边缘反光 -> 最外圈暗边，制造厚度感。
            //
            // 四个 stop 逐个看（括号里是 RGB，值越大越亮）：
            //   0.00  #1A1B28 (26,27,40)   最亮的中心灰 —— 光源正对的那块
            //   0.62  #0D0E16 (13,14,22)   主盘面，全图最暗，占的面积也最大
            //   0.86  #2A2B3C (42,43,60)   一圈回光，模拟黑胶沟槽对光的漫反射
            //   1.00  #12131D (18,19,29)   外缘压暗，让圆盘有个收边的轮廓
            // 亮度曲线是「亮 -> 暗 -> 亮 -> 暗」，第二亮的那一段（0.86）就是「黑胶反光」的来源：
            // 纯渐暗会像一块塑料板，加一圈回光之后才像有沟槽的唱片。
            //
            // Center (0.5,0.5) 是渐变原点、GradientOrigin (0.38,0.32) 是高光位置，
            // 两者错开才形成偏心光晕；若都写 0.5，高光正对圆心，看起来像正上方打了盏平光灯。
            // 这两个点都是 RelativeUnit.Relative，即相对「被填充椭圆的包围盒」。
            //
            // RadiusX / RadiusY = 0.55（相对包围盒的比例）：0.5 大约对应内切半径，
            // 0.55 略大于它，意味着渐变的终点落在圆盘外沿之外一点，
            // 盘边实际取到的是 0.86 与 1.0 两个 stop 之间的颜色（接近 #12131D），
            // 所以最后那一档「外缘压暗」只有很薄的一圈能看到。
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
            //
            // 半径递进的算法：rr = r * (0.55 + i * 0.1)，i 从 1 取到 4
            //   i=1 -> 0.65r    i=2 -> 0.75r    i=3 -> 0.85r    i=4 -> 0.95r
            //   相邻两圈恒差 0.1r，所以是严格等距的四条同心圆。
            //   循环从 1 开始（不是 0）是有意的：i=0 会得到 0.55r，
            //   那一圈离中心标签（0.245r）太近、又离第一条真正纹路太远，会显得疏密不均。
            //   算例 r=100：半径 65 / 75 / 85 / 95，间距 10px，最外圈离盘边还有 5px。
            //
            // 一个容易忽略的事实：这四圈是同心圆，而同心圆在旋转下完全不变。
            // 也就是说「唱片在转」这件事，纹路一点都体现不出来，
            // 真正能看出来的只有中心标签的紫青渐变（以及盘体渐变）在转。
            var groove = new Pen(new SolidColorBrush(Colors.White, 0.045), 1);
            for (var i = 1; i <= 4; i++)
            {
                var rr = r * (0.55 + i * 0.1);
                context.DrawEllipse(null, groove, center, rr, rr);
            }

            // 中心标签
            // 45° 对角渐变（紫 -> 青）是品牌色，半径 0.245r 大致对应真实唱片标签的比例。
            //   真实 12 英寸黑胶的标签直径约 4 英寸、盘面 12 英寸，比值 1/3 ≈ 0.333；
            //   这里 0.245 略小一些，是为了给四圈纹路留出更多可见面积
            //   （标签占 0.245r，第一圈纹路在 0.65r，中间有 0.4r 的空档展示沟槽）。
            // 渐变的 StartPoint(0,0) -> EndPoint(1,1) 是左上到右下，两个点同样是相对包围盒的比例，
            // 所以旋转时这道对角高光会跟着转 —— 这正是「看得出唱片在转」的主要视觉线索。
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
    //
    // 展开讲这个参数在属性系统里究竟改了什么：
    //   1) 绑定的方向由 BindingMode 决定：OneWay 是「源 -> 目标」，TwoWay 是「双向」；
    //   2) XAML 里写 {Binding Progress} 而不写 Mode 时，绑定引擎会去查属性注册表：
    //      「这个属性的默认绑定模式是什么」，答案就是注册时传的 defaultBindingMode。
    //      绝大多数 StyledProperty 的默认值是 OneWay，所以不显式指定就会「只读」。
    //   3) 于是这里的 TwoWay 不是「让控件可拖」，而是「让 SetValue(ValueProperty, x) 这个动作
    //      顺着绑定把值写回 VM 的属性」。写回与这次属性变更在同一次通知里完成，
    //      VM 的 setter 立即执行，由它派生的显示文本（如 03:12 / 04:58）也随之刷新。
    //   4) 两个前提：必须真的存在绑定；且源属性必须可写（只读属性只能 OneWay）。
    //      直接 new 一个 TrackBar 再赋值 Value，即使注册成 TwoWay 也不会回写到任何地方。
    //   5) 优先级照旧：本地值 > 样式 > 继承 > 默认值。绑定写入的是「本地值」这一档，
    //      因此在绑定生效期间，样式里给 Value 设的初始值会被覆盖。
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

    // 构造期只做两件与交互有关的小事，几何全部在 Render 里按 Bounds 现算，
    // 所以不存在「构造时尺寸未知」的问题。
    public TrackBar()
    {
        // 不参与焦点遍历：这类条状控件若可聚焦，Tab 键会在每个进度条上停留一次，体验很差。
        // Focusable 属于 Control 自带属性，改它不影响键盘事件——不聚焦就收不到 KeyDown，
        // 这也是本控件「不支持键盘调节」的直接原因（见类顶部注释里的约束）。
        Focusable = false;
        // 手型光标提前告知「这里可以拖」，比悬停时才变光标更明确。
        // Cursor 是继承属性：子级没设时会沿用父级的设置，但这里显式设在本控件上，
        // 所以只有指针进入 TrackBar 的区域才变手型，离开即恢复。
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
    // ===== 一次完整拖动的生命周期（四步，全部由指针捕获串起来）=====
    //   1) PointerPressed：_dragging = true；Capture(this)；立刻按点击位置设定 Value。
    //      捕获之后，本控件成为指针的「捕获者」，后续 Moved / Released 事件即使发生在控件边界之外
    //      也会路由到本控件（而不是落到下面的兄弟控件上）。
    //   2) PointerMoved：若 _dragging 为真，用当前 x 重新换算 Value。
    //      这一步在手指/鼠标移出控件后仍然有效，正是因为有捕获。
    //   3) PointerReleased：_dragging = false；Capture(null) 释放；重绘隐藏滑块。
    //      释放必须做，否则这个控件会一直「吃」掉后续的指针事件，整个窗口的点击都会失灵。
    //   4) 渲染：Value 变化触发 AffectsRender -> InvalidateVisual，滑块位置随之更新。
    //      整个拖动过程没有任何显式的 InvalidateVisual 调用（只在按下/抬起时各补一次，
    //      那两次是为了让「点击跳转」和「隐藏滑块」立即生效，而不是等下一次 Value 变化）。
    //
    // 未覆盖的一环（如实记录）：本类没有重写 OnPointerCaptureLost。
    // 如果捕获被外部原因夺走（窗口失活、弹出模态层、另一个控件抢捕获），
    // 就不会有 PointerReleased，_dragging 会残留为 true，
    // 结果是滑块一直显示、并且下次移动指针时仍会改 Value。补法是重写该方法把 _dragging 置 false。
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _dragging = true;
        // Capture 让后续 Moved/Released 事件即使指针移出控件边界也继续送给本控件，
        // 否则用户快速横向拖动会中途丢失跟踪、滑块「粘」在原地。
        // 传 null 表示释放；同一个指针同时只能有一个捕获者，新控件 Capture 会顶掉旧的。
        e.Pointer.Capture(this);
        // 先定位再重绘：支持「点哪跳哪」，而不是只能从当前滑块位置开始拖。
        // e.GetPosition(this) 返回的是「相对本控件左上角」的坐标（单位是与 DPI 无关的逻辑像素），
        // 与 SetFromPoint 里 x/w 的归一化方式完全对应 —— 若换成 GetPosition(null)（相对窗口），
        // 归一化就会算错，滑块会跳到控件外面去。
        SetFromPoint(e.GetPosition(this).X);
        InvalidateVisual();
        // 标记已处理，避免外层（如播放器整块点击区域）再响应同一次按下。
        // e.Handled = true 只影响冒泡阶段的上层控件，不影响本控件后续的 Moved/Released。
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
    // 调用者：OnPointerPressed（点哪跳哪）与 OnPointerMoved（拖动跟随），两处都只传 x。
    // 前置状态：Bounds 必须已经完成一次 Arrange，否则 w 为 0；
    // 后置状态：Value 被写入 -> 若存在 TwoWay 绑定，值会顺着绑定写回 ViewModel
    //           -> ValueProperty 在 AffectsRender 名单里，Avalonia 自动重绘。
    //
    // 归一化的两步，代一组具体数字：
    //   ratio = x / w，再 Clamp 到 [0,1]
    //     设 w = 300：x = 150 -> ratio = 0.5（正中间）
    //                  x = -12 -> ratio = -0.04 -> Clamp 成 0（拖到左边界外）
    //                  x = 372 -> ratio = 1.24  -> Clamp 成 1（拖到右边界外）
    //   Value = Minimum + ratio * (Maximum - Minimum)
    //     设 Minimum=0、Maximum=100：ratio 0.5/0/1 分别得到 50/0/100。
    //     区间不是 0..100 时同样成立，例如 Minimum=20、Maximum=80：
    //     ratio=0.5 -> 20 + 0.5*60 = 50。
    //
    // 两点容易误解的地方：
    //   · 这里假设轨道占满整个控件宽度（0..w），而 Render 里滑块中心会被钳制到 [5, w-5]。
    //     所以控件边缘的 5px 内「点击换算」和「滑块显示」并不完全一致（见 Render 里的说明）。
    //   · 连续移动会连续写 Value。Avalonia 的属性系统在「新旧值相等」时会直接返回，
    //     不触发变更通知，所以原地抖动不会产生多余的重绘与绑定写回。
    private void SetFromPoint(double x)
    {
        var w = Bounds.Width;
        // 布局尚未完成时宽度可能为 0，直接返回避免除零得到 NaN。
        // 一旦算出 NaN，Value 会变成 NaN，而 Math.Clamp(NaN, ...) 不会把它救回来，
        // 后续所有绘制都会失效 —— 这个提前返回挡住的是这类连锁反应。
        if (w <= 0) return;
        var ratio = Math.Clamp(x / w, 0, 1);
        Value = Minimum + ratio * (Maximum - Minimum);
    }

    /// <summary>
    /// 绘制入口：底轨 -> 已填充部分 -> 悬停/拖动时的白色圆点。
    /// </summary>
    // 触发者：AffectsRender 登记的五个属性（Value / Maximum / Minimum / Fill / TrackHeight）任一变化，
    // 以及 OnPointerEntered / Exited（悬停状态变化时不必改属性，直接重绘即可）。
    // 前置状态：只需要 Bounds 已确定；Value 可能超出 [Minimum, Maximum]（外部直接赋值时不受约束）。
    // 后置状态：不写字段，纯绘制。
    // 绘制顺序就是图层顺序：底轨 -> 填充 -> 滑块，后画的盖住先画的。
    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        // 只判宽度不判高度：高度为 0 时下面算出来的矩形是零高，画不出东西也不会崩，
        // 而宽度为 0 时 fillW 恒为 0，同样无害 —— 这个判断主要是省掉一次无意义的绘制。
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
        // 归一化：ratio = (Value - Minimum) / span，再 Clamp 到 [0,1]。
        //   减去 Minimum 是因为区间不一定从 0 开始（音量、温度都可能从非零起步）。
        //   Clamp 兜住两种异常：外部把 Value 赋成超界值（例如 120），
        //   以及 Minimum > Maximum 的错误配置（此时 span 被兜成 1，ratio 会算出负数或大于 1）。
        var ratio = Math.Clamp((Value - Minimum) / span, 0, 1);
        // 填充宽度 = 控件宽度 × 比例。这意味着 100% 时填充正好铺满到 x=w，
        // 与底轨（0..w）同长；滑块中心也在 w 附近（被钳到 w-5），三者基本对齐。
        var fillW = w * ratio;
        // 宽度小于 0.5 时不画，避免开头出现一个几乎不可见的圆角毛刺。
        // 圆角半径是 th/2（默认 2），一个宽度 0.3 的矩形画出来会是两个重叠的圆角，
        // 边缘还有抗锯齿的半透明像素，看起来比「没有」更脏。
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
        // IsPointerOver 是 InputElement 自带的状态属性，由 Avalonia 根据指针位置自动维护，
        // 本类不需要（也无法）手动赋值；OnPointerEntered / OnPointerExited 只负责在它变化后重绘一次。
        // _dragging 必须一起判断：指针被捕获后即使移出控件，IsPointerOver 会变成 false，
        // 但拖动还在进行，此时滑块不能消失。
        if (IsPointerOver || _dragging)
        {
            // 钳制到 [5, w-5]：保证半径为 5 的圆点在 0% 和 100% 时仍完整可见，不会被控件边界裁掉一半。
            // 这里隐含要求控件宽度不小于 10，否则 Clamp 的下界会大于上界而抛 ArgumentException。
            //
            // 注意 Math.Clamp 的区间是闭区间：下界 5 对应「圆心距左边 5」，正好让圆与左边框相切；
            // 上界 w-5 同理让圆与右边框相切。
            //
            // 与直觉不符的一处：滑块中心直接取 fillW，而 fillW 没有做过钳制。
            // 于是在 Value = Minimum 时填充宽度是 0、滑块中心被钳到 5，
            // 在 Value = Maximum 时填充末端在 w、滑块中心被钳到 w-5。
            // 也就是两端各存在最多 5px 的「滑块中心与填充末端不重合」，
            // 视觉上滑块略微越出填充区 —— 因为滑块本身半径就是 5，这样反而正好盖住端点。
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

    // 本控件可能同时在列表里出现几十个（每行一个「正在播放」标记），
    // 所以计时器周期最省：8fps。构造里只创建不启动，避免未显示的实例白跑。
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
        // 前置状态：_t 由 120ms 计时器推进；控件尺寸通常只有 12~14px（列表行内）。
        // 后置状态：不写字段，纯绘制。
        // 触发者：只有计时器 Tick 里的 InvalidateVisual —— 与 AudioBars 一样没有属性驱动。
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 2 || h <= 2) return;

        var brush = new SolidColorBrush(Color.Parse("#6C8BFF"));
        // 柱宽固定 2px，剩余宽度平均分成两段间隙，因此整体在控件内左中右均布。
        //
        // 算式：gap = (w - 3*barW) / 2 = (w - 6) / 2，两根间隙，每根柱子之间隔一个 gap。
        // 位置：第 i 根柱的 x = i * (barW + gap)
        //   算例 w = 14：gap = (14-6)/2 = 4，x = 0 / 6 / 12，
        //                第三根占 12..14，右边缘正好贴住控件右边，左边第一根贴住左边 —— 左右都不留白。
        //   算例 w = 12：gap = 3，x = 0 / 5 / 10，第三根占 10..12。
        //   w 小于 6 时 gap 变成负数，柱子会互相重叠（但 w<=2 已被上面的判断挡掉，
        //   所以只有 3..6 这种极端宽度才会出现重叠）。
        const double barW = 2;
        var gap = (w - barW * 3) / 2;

        for (var i = 0; i < 3; i++)
        {
            // sin 值域 [-1,1] 先映射到 [0,1]，再压到 0.35~1.0：柱子始终保留 35% 高度，不会完全消失。
            // i * 1.1 的相位差让三根柱子轮流到达波峰，看起来像均衡器在跳动。
            //
            // level 的完整算式：0.35 + 0.65 * (sin(_t + i*1.1) * 0.5 + 0.5)
            //   sin(...) * 0.5 + 0.5  把 [-1,1] 映射成 [0,1]，记作 t；
            //   0.35 + 0.65 * t       再把 [0,1] 拉到 [0.35, 1.0]：
            //                         t=0 -> 0.35（最矮，仍有 35% 高），t=1 -> 1.0（顶满控件）。
            //   为什么不映射到 [0,1]：柱子缩到 0 会让指示器在一瞬间「消失」，
            //   在 12px 的尺寸下看起来像闪烁，保留一个下限才像「持续在播」。
            //
            // 相位差 i * 1.1（弧度，约 63°）造成的错落感：
            //   三根柱子的相位分别是 0、63°、126°。
            //   正弦周期 360°，所以三根柱子依次到达波峰、相隔 63°（约 0.175 个周期）。
            //   当前 _t 每 120ms 加 0.35 弧度，走完 63° 需要 1.1/0.35 ≈ 3.1 个 Tick（约 0.37 秒），
            //   于是肉眼看到的是「左 -> 中 -> 右」依次起伏，像一段连续的波浪扫过去。
            //   如果相位差取 0，三根柱子会完全同步，看起来只是一根粗细变化的柱子；
            //   取 2.09（120°）则三根柱子的相位刚好均分整个周期，任意时刻总有一根在高位、一根在低位。
            var level = 0.35 + 0.65 * (Math.Sin(_t + i * 1.1) * 0.5 + 0.5);
            // barH 直接按控件高度换算；Math.Max(2, ...) 的托底在 level 最低 0.35 时根本用不到，
            // 只有 h < 5.7 这种极小尺寸才会触发 —— 属于防御性代码。
            var barH = Math.Max(2, h * level);
            // 底部对齐（y = h - barH），圆角 1 让 2px 宽的细柱两端略微软化。
            // 圆角 1 等于柱宽的一半，于是柱子的上下两端都是半圆（胶囊形）；
            // 因为底边刚好在 y=h，下半圆的弧会略微超出控件底边，被父容器裁掉 —— 视觉上正好是齐平的。
            context.DrawRectangle(brush, null, new Rect(i * (barW + gap), h - barH, barW, barH), 1, 1);
        }
    }
}
