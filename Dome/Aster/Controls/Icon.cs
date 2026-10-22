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
    public override void Render(DrawingContext context)
    {
        if (_geometry is null) return;

        var w = Bounds.Width;
        var h = Bounds.Height;
        // 尺寸为 0 时直接返回：此时 scale 为 0、几何会退化成一个点，绘制没有意义（布局初期会短暂出现）。
        if (w <= 0 || h <= 0) return;

        // 未设置颜色时用白色兜底，保证在深色底上至少可见，而不是画出一个透明图标。
        var brush = Foreground ?? Brushes.White;
        // 取 min(w,h) 而不是分别缩放 w/h：图标必须保持等比，否则 16x20 这类非方形尺寸会把图形拉扁。
        var scale = Math.Min(w, h) / 24.0;
        // 等比缩放后 24*scale 可能小于控件宽高，剩余空间平均分到两侧即为居中偏移。
        var dx = (w - 24 * scale) / 2;
        var dy = (h - 24 * scale) / 2;

        // Avalonia 的矩阵按「行向量 * 矩阵」组合，A * B 表示先做 A 再做 B：
        // 这里即 先缩放（把 24 单位放大到目标尺寸）→ 再平移（移到居中位置）。
        // using 负责在绘制结束后弹出变换栈，避免影响后续兄弟元素的绘制。
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(dx, dy)))
        {
            if (Filled)
            {
                // 实心模式：只填充，不描边；Thickness 在此被忽略。
                context.DrawGeometry(brush, null, _geometry);
            }
            else
            {
                // 线性图标：只描边。Round 端点/连接让 1.6px 的细线在小尺寸下转角更柔和，
                // 也更接近设计稿中手绘风格的圆头线条。
                var pen = new Pen(brush, Thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                context.DrawGeometry(null, pen, _geometry);
            }
        }
    }
}
