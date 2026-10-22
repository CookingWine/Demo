// 文件：Icon.cs
// 作用：把 24x24 的 SVG path 字符串渲染成任意尺寸的矢量图标，是整套 UI 的图形基础。
// 使用方：各视图以 <ctrl:Icon Data="{x:Static ctrl:Icons.Grid}" Width="16" Height="16"/> 直接引用。
// 思路：Data 变化时 Geometry.Parse 解析并缓存 -> Render 中按 min(w,h) 等比缩放并居中平移 -> 填充或描边。
// 精度：全部为矢量绘制，任意 DPI 下都清晰；几何只在数据变化时重建，尺寸变化仅改变换矩阵。
// 性能：几何缓存 + AffectsRender 自动重绘，避免每帧解析 path 产生额外分配。
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Aster.Controls;

/// <summary>
/// 极简矢量图标：以 24x24 的 SVG path 数据绘制，颜色跟随继承的 Foreground。
/// </summary>
// 之所以固定 24x24 基准：Icons.cs 中的 path 全部按该坐标系手写，换尺寸只缩放不重写数据。
// 控件自身不参与测量，尺寸完全由外部 Width/Height 决定（Stretch 类行为交给布局容器）。
public class Icon : Control
{
    // 解析后的几何缓存：path 解析相对昂贵，只在 Data 变化时重建。
    private Geometry? _geometry;

    /// <summary>SVG path 数据（24x24 坐标系），默认 null。</summary>
    public static readonly StyledProperty<string?> DataProperty =
        AvaloniaProperty.Register<Icon, string?>(nameof(Data));

    /// <summary>描边线宽，默认 1.6；Filled 为 true 时该值不生效。</summary>
    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(Thickness), 1.6);

    /// <summary>是否填充绘制，默认 false（即描边）。用于播放三角、省略号等实心图形。</summary>
    public static readonly StyledProperty<bool> FilledProperty =
        AvaloniaProperty.Register<Icon, bool>(nameof(Filled));

    // 借用 TextElement.ForegroundProperty 并 AddOwner，而不是新注册一个属性：
    // 这样 Icon 会沿用 Avalonia 属性继承链，自动拿到外层 TextBlock/Border 的 Foreground。
    // 若单独 Register，Foreground 不会继承，图标在浅色主题下会突然变成固定颜色。
    /// <summary>图标颜色；未显式设置时继承外层 Foreground。</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Icon>();

    // 静态构造中集中登记「影响渲染」的属性：任一属性变化由 Avalonia 自动调用 InvalidateVisual，
    // 省去在每个 setter 里手写重绘逻辑，也不会漏掉样式/绑定来源的修改。
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

    /// <summary>图标的 24x24 SVG path；设为 null 时不绘制任何内容。</summary>
    public string? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>描边线宽，默认 1.6；数值越小在高 DPI 下越接近设计稿的细线。</summary>
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

    /// <summary>
    /// 属性变化回调：仅在 Data 变化时重新解析几何，其余属性交给 AffectsRender 触发重绘。
    /// </summary>
    // 调用时机：任意 StyledProperty 变更都会走到这里（包括本地值、样式 setter、绑定、动画产生的变更），
    // 所以第一件事必须是 base.OnPropertyChanged，再做 change.Property 精确过滤。
    // 若省掉过滤，Foreground 或 Thickness 每变一次都会白解析一次 path，纯属浪费。
    //
    // 与 AffectsRender 的分工（两条独立通道，互不冲突）：
    //   1) 本回调：重建「昂贵的派生数据」——Geometry 对象。只在 Data 变化时做一次。
    //   2) AffectsRender：静态构造里登记的四个属性由 Avalonia 自动调用 InvalidateVisual，
    //      它只把控件标记成「待重绘」，真正的 Render 要等下一帧的渲染循环。
    //   因为 Render 一定晚于本次属性变更通知，所以两条通道谁先谁后都不影响结果：
    //   Render 执行时 _geometry 必然已经是最新值。
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataProperty)
        {
            // 先清空再解析：即使新数据非法（例如空串或格式错误），也不会残留上一个图标。
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
                    // 吞掉解析异常是刻意为之：图标数据多由常量表提供，个别写错不应让整个界面崩溃，
                    // 静默降级为「不绘制」，问题在开发期通过视觉缺失即可发现。
                    _geometry = null;
                }
            }
        }
    }

    /// <summary>
    /// 绘制入口：把 24x24 坐标系等比缩放并居中到当前 Bounds，再按 Filled 选择填充或描边。
    /// </summary>
    // 调用链：Avalonia 渲染管线在三种情况下调用本方法——
    //   a) 控件首次 Measure/Arrange 完成后第一次出图；
    //   b) 静态构造登记的四个属性（Data / Thickness / Filled / Foreground）任一变化，
    //      经 AffectsRender 触发 InvalidateVisual，下一帧进入 Render；
    //   c) 外部显式 InvalidateVisual()（本类自身没有调用）。
    //
    // 前置状态：_geometry 已由 OnPropertyChanged 在 Data 变化时准备好，可能为 null
    // （Data 从未赋值，或赋值了一串 Geometry.Parse 抛异常的非法 path）。
    // 后置状态：本方法不写任何字段，只向 DrawingContext 提交绘制指令；
    // 变换用 using 压栈、离开作用域弹栈，因此退出后绘图坐标系与进入时完全一致，
    // 同一次渲染批次里后续的兄弟控件不会被「缩放 0.667」之类的残留矩阵影响。
    public override void Render(DrawingContext context)
    {
        // 步骤 1：没有任何几何可画就立刻返回。这是唯一的「无内容」出口，
        // 上面说的两种 null 来源在这里被统一处理，调用方无需判断 Data 是否合法。
        if (_geometry is null) return;

        // 步骤 2：取控件在 Arrange 阶段定下的最终尺寸。
        // 注意 Bounds 不等于 XAML 里写的 Width/Height：那只是「期望值」，
        // 父容器（Grid 的 * 列、StackPanel 的拉伸）可能把它压缩或放大，Bounds 才是真实像素。
        var w = Bounds.Width;
        var h = Bounds.Height;
        // 尺寸为 0 时直接返回：此时 scale 为 0、几何会退化成一个点，绘制没有意义（布局初期会短暂出现）。
        // 顺带挡住负数尺寸（理论上不会出现，但 Math.Min 拿到负数会让矩阵变成镜像）。
        // 布局时序上，构造完成到第一次 Arrange 之间 Bounds 是 (0,0,0,0)，这条判断就是为那一帧准备的。
        if (w <= 0 || h <= 0) return;

        // 步骤 3：选画刷。未设置颜色时用白色兜底，保证在深色底上至少可见，而不是画出一个透明图标。
        // 之所以敢兜底白色：本工程的图标层全部画在深色底（#0A0B12 一类的背景）上。
        var brush = Foreground ?? Brushes.White;

        // 步骤 4：算缩放系数 scale = min(w, h) / 24。逐层拆开看：
        //   · 为什么除以 24：Icons.cs 里的 path 全部画在 24x24 的逻辑画布上，
        //     要把「逻辑单位」换成「像素」，比例只能是 目标边长 / 24。
        //   · 为什么取 min(w,h) 而不是分别用 w/24 和 h/24：分别缩放会把非方形控件里的图标拉扁，
        //     取较小者等价于 CSS 的 object-fit: contain——保证 24 单位方框整体塞进 Bounds，
        //     多出来的方向留白。
        //
        // 手算四个例子（scale 与两个偏移一起算，便于在纸上复算）：
        //
        //   例 A：Bounds = 16 x 16
        //     scale = min(16,16)/24 = 16/24 ≈ 0.6667
        //     24 * scale = 16           -> 图形正好占满 16x16，没有剩余空间
        //     dx = (16 - 16)/2 = 0
        //     dy = (16 - 16)/2 = 0      -> 偏移为 0，24 单位直接映射到 0..16
        //
        //   例 B：Bounds = 16 x 20（竖向更宽松的按钮槽位）
        //     scale = min(16,20)/24 = 16/24 ≈ 0.6667
        //     24 * scale = 16           -> 只占满宽度方向，纵向不够 20
        //     dx = (16 - 16)/2 = 0
        //     dy = (20 - 16)/2 = 2      -> 上下各留 2px，图形垂直居中
        //     若错误地写成 h/24 = 0.8333，图形会被拉成 20 单位高、纵向变形 25%
        //
        //   例 C：Bounds = 20 x 16（横向更宽松，结果与 B 镜像）
        //     scale = 0.6667，dx = 2，dy = 0
        //
        //   例 D：Bounds = 48 x 48（2 倍尺寸）
        //     scale = 2，24 * scale = 48，dx = dy = 0，逻辑 1 单位 = 2 像素
        var scale = Math.Min(w, h) / 24.0;

        // 步骤 5：算居中偏移。
        //   24 * scale 是缩放后图形真正的占位边长；
        //   w 减去它就是左右两侧的剩余空白，除 2 把空白均分到两侧，
        //   于是图形中心与控件中心重合（几何坐标系原点在控件左上角，所以要加偏移而不是减）。
        //   由于 scale 取的是 min(w,h)，这两个式子里必有一个恰好为 0：
        //   较窄的那个方向贴边、较宽的方向居中。
        // 等比缩放后 24*scale 可能小于控件宽高，剩余空间平均分到两侧即为居中偏移。
        var dx = (w - 24 * scale) / 2;
        var dy = (h - 24 * scale) / 2;

        // 步骤 6：压入「先缩放、后平移」的复合矩阵。
        //   Avalonia 的 Matrix 用行向量约定：点 p 变换后写作 p * M，
        //   于是 A * B 表示「先作用 A、再作用 B」（和列向量写法 M_B * M_A 的顺序正好相反，极易记错）。
        //   把例 B 的数字代进去，逻辑坐标 (12,12)（画布中心）的走向是：
        //     (12,12) --缩放 0.6667--> (8,8) --平移 (0,2)--> (8,10)
        //   而控件中心是 (8,10)（16 宽 20 高的一半），图形中心正好落在这里。
        //   如果两个矩阵乘反（先平移再缩放），平移量会被随后的缩放一起缩小：
        //   代入例 B 得 (0,0) -> (0,1.333)、(12,12) -> (8,9.333)，
        //   图形仍占 16x16，但整体比正确位置高了 0.667px，不再垂直居中；
        //   dx 非零的例 C 同理，横向也会偏。尺寸越小偏得越明显。
        //
        // 步骤 7：PushTransform 的返回值是一层「变换栈帧」。
        //   它内部记着压栈前的 context.Transform，Dispose 时把旧矩阵写回去，等价于弹栈。
        //   这一步绝不能省：DrawingContext 在一次渲染批次里是跨控件复用的
        //   （父容器画完接着画子控件），矩阵不弹回去就会泄漏给后面所有绘制，
        //   整个界面会被连带缩小 0.667 倍并平移。
        //   本方法只有一个 using，所以严格一压一弹；像 Icons 那种多图元控件会在循环里反复压弹。
        //
        // Avalonia 的矩阵按「行向量 * 矩阵」组合，A * B 表示先做 A 再做 B：
        // 这里即 先缩放（把 24 单位放大到目标尺寸）→ 再平移（移到居中位置）。
        // using 负责在绘制结束后弹出变换栈，避免影响后续兄弟元素的绘制。
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(dx, dy)))
        {
            // 步骤 8：两条互斥分支，最终像素差异如下。
            //
            //   Filled = true —— DrawGeometry(brush, null, ...)，第一个参数是填充、第二个是描边（null 表示不描边）：
            //     path 围出的内部整块涂成 brush 颜色，边界完全由几何轮廓决定，
            //     Thickness 在这里被彻底忽略（传什么都不影响结果）。
            //     视觉上「更重、更实」，适合 Play 三角、Pause 双竖条、More 三个点这类
            //     用描边表达会变成空心框的图形。
            //
            //   Filled = false —— DrawGeometry(null, pen, ...)，只描边不填充：
            //     path 变成一条 brush 颜色的「线」，线宽 Thickness，内部保持透明，
            //     因此图标是镂空的，会透出下面的背景色（这也是图标能跟随任意底色的原因）。
            //     Round 线帽 + Round 线连接让闭合路径的转角没有硬尖：
            //     16px 图标上线宽只有约 1px，方形拐角几乎看不出细节，磨圆之后反而更清晰。
            //
            //   像素宽度换算（容易被忽略的一点）：
            //     Pen 是在 PushTransform 之后创建的，绘制时同样会乘上 scale，
            //     所以 Thickness=1.6 在 16px 图标上实际只有 1.6*0.6667 ≈ 1.07 设备像素，
            //     在 48px 图标上是 3.2 像素。
            //     也就是说 1.6 是「24 逻辑单位」下的线宽而不是屏幕像素，这是刻意的：
            //     线宽随图标等比变化，不同尺寸下的视觉粗细比例保持一致。
            if (Filled)
            {
                // 实心模式：只填充，不描边；Thickness 在此被忽略。
                context.DrawGeometry(brush, null, _geometry);
            }
            else
            {
                // 线性图标：只描边。Round 端点/连接让 1.6px 的细线在小尺寸下转角更柔和，
                // 也更接近设计稿中手绘风格的圆头线条。
                // Pen 每帧新建一次：Avalonia 的 Pen 是轻量值对象（不是 Drawing 对象），
                // 分配开销远小于缓存一个可变字段带来的状态同步成本。
                var pen = new Pen(brush, Thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                context.DrawGeometry(null, pen, _geometry);
            }
        }
        // 说明（本类没有做、但需要知道）：Control 默认 ClipToBounds = false，
        // 所以描边在理论上可以画到 Bounds 之外，不会被自动裁掉。
        // Icons.cs 里的路径都收在 3..21 之间，加半个线宽 0.8 也只到 21.8，
        // 仍在 24 网格内，因此实际不会溢出。
        // 若将来加入贴边（0 或 24）的路径，需要自己给 Icon 设 ClipToBounds，
        // 或者把坐标往内收，否则圆头线帽会越界 0.8 个逻辑单位。
    }
}
