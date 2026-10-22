// 文件：MarkdownPreview.cs
// 作用：不依赖 WebView、不引 Markdown 库的轻量预览器，把 Markdown 文本转成原生 Avalonia 控件树。
// 使用方：笔记/文档详情页，XAML 中 <ctrl:MarkdownPreview Markdown="{Binding Content}"/>。
// 实现：继承 ScrollViewer 提供滚动，Content 固定为一个 StackPanel；Build() 逐行扫描并按语法挂载子控件。
// 取舍：Markdown 每次变化都清空重建整棵树——实现简单、不会残留上一次的解析状态；
//       文档量级在几百行内，重建成本远低于一次布局耗时，因此完全可接受。
// 支持范围：标题 / 引用 / 无序列表 / 表格 / 围栏代码块，以及行内 **加粗** 与 `代码`；其余按普通段落呈现。
// 精度：表格列宽按列数均分（Star）而非测量内容宽度，长文本会换行而不是撑宽表格。
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Aster.Controls;

/// <summary>
/// 轻量 Markdown 预览（标题 / 引用 / 列表 / 表格 / 代码块 / 行内加粗与代码）。
/// 不依赖 WebView，纯 Avalonia 控件树渲染，风格对齐设计稿。
/// </summary>
// 为什么不套 UserControl + ScrollViewer：直接继承可以少一层视觉树，
// 外部还能拿到 VerticalOffset 等滚动 API；Padding 同时作用于内容区，滚动条仍贴控件边缘。
// 内容容器固定为 StackPanel：所有块级元素纵向堆叠，元素自身的 Margin 负责段间距。
public class MarkdownPreview : ScrollViewer
{
    private readonly StackPanel _root = new() { Spacing = 0 };

    /// <summary>Markdown 源文本，默认 null；为空白时内容区为空。</summary>
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownPreview, string?>(nameof(Markdown));

    // AffectsRender 标记该属性变化需要重绘；但内容实际由控件树承载，
    // 真正的重建放在 OnPropertyChanged 里，两者各司其职。
    static MarkdownPreview()
    {
        AffectsRender<MarkdownPreview>(MarkdownProperty);
    }

    public MarkdownPreview()
    {
        // 直接把 StackPanel 设为 Content，避免再包一层 Grid/UserControl。
        Content = _root;
        // 禁用横向滚动：表格与代码块自行处理换行，若允许横向滚动会与纵向滚动条争抢空间。
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        // 内边距刻意做成不对称：底部留 60 让最后一行不贴住窗口下沿。
        Padding = new Thickness(26, 24, 28, 60);
    }

    /// <summary>Markdown 源文本，默认 null。</summary>
    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>
    /// 属性变化回调：Markdown 变化时整棵控件树重建，其余属性交给基类处理。
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty) Build();
    }

    /// <summary>
    /// 首次挂到可视树时补一次 Build：属性可能在控件构造前就已通过 XAML 赋值，
    /// 那时 OnPropertyChanged 已经执行过，但布局尚未开始；这里确保最终内容一定是最新的。
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Build();
    }

    // 设计令牌色值直接内联：这些画刷只在解析时构造一次并复用，抽成资源字典反而增加查找成本。
    // 色阶由亮到暗：正文标题 Text0 -> 段落 Body -> 次要 Muted，强调色为主色与紫色。
    private static readonly IBrush Text0 = new SolidColorBrush(Color.Parse("#F2F4FA"));
    private static readonly IBrush Body = new SolidColorBrush(Color.Parse("#BFC7D9"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#A9B1C6"));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#6C8BFF"));
    private static readonly IBrush Violet = new SolidColorBrush(Color.Parse("#A78BFA"));
    private static readonly IBrush CodeBrush = new SolidColorBrush(Color.Parse("#A8BAFF"));
    // 等宽字体回退链：优先 JetBrains Mono，其次 macOS / Windows 常见等宽字体，最后退回 monospace。
    private static readonly FontFamily Mono = new("JetBrains Mono, SF Mono, Consolas, Menlo, monospace");

    /// <summary>
    /// 逐行状态机：从上到下扫描，根据行首特征决定生成哪种块级控件，并推进索引 i。
    /// 每个分支结束后都必须推进 i，否则会死循环——代码块与表格是唯一会一次吞掉多行的分支。
    /// </summary>
    private void Build()
    {
        // 整棵树先清空：不做增量 diff，避免旧内容残留（例如从长文档切到短文档）。
        _root.Children.Clear();
        var text = Markdown;
        if (string.IsNullOrWhiteSpace(text)) return;

        // 统一换行符再切分：Windows 的 \r\n 与 Unix 的 \n 走同一套逻辑，避免行尾残留 \r 影响 StartsWith 判断。
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];
            // 只去尾部空白而保留行首缩进：行首空格在 Markdown 里可能有语义（虽然本实现未处理嵌套）。
            var trimmed = line.TrimEnd();

            // 空行：用一个 10px 高的空 Border 充当段间距，比给每个块加 margin 更容易统一控制。
            if (trimmed.Length == 0)
            {
                _root.Children.Add(new Border { Height = 10 });
                i++;
                continue;
            }

            // 代码块
            // 遇到 ``` 开始收集，直到下一个以 ``` 开头的行（语言标识直接忽略）；
            // 内层 while 结束时 i 停在结束围栏上，再 i++ 跳过它，避免把围栏当成普通段落。
            if (trimmed.StartsWith("```"))
            {
                var code = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                {
                    code.Add(lines[i]);
                    i++;
                }
                i++;
                // 用换行符重新拼回多行文本：代码行按原样收集，保留缩进与空行，只丢掉首尾两条围栏。
                _root.Children.Add(CodeBlock(string.Join("\n", code)));
                continue;
            }

            // 标题
            // 先判断 ### 再判断 ## 再判断 #：否则 "### x" 会被 "## " 前缀命中，标题层级错位。
            if (trimmed.StartsWith("### "))
            {
                _root.Children.Add(Heading(trimmed[4..], 14.5, Text0, false));
                i++;
                continue;
            }
            if (trimmed.StartsWith("## "))
            {
                _root.Children.Add(Heading(trimmed[3..], 16, new SolidColorBrush(Color.Parse("#E4E8F2")), true));
                i++;
                continue;
            }
            if (trimmed.StartsWith("# "))
            {
                // 一级标题额外带一条细分隔线，用 StackPanel 竖排文字与线条。
                var h1 = new StackPanel { Spacing = 14, Margin = new Thickness(0, 0, 0, 6) };
                h1.Children.Add(InlineText(trimmed[2..], 24, Text0, FontWeight.SemiBold));
                h1.Children.Add(new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Colors.White, 0.065)
                });
                _root.Children.Add(h1);
                i++;
                continue;
            }

            // 引用
            // 去掉开头的 > 后再 Trim，支持 "> 文本" 与 ">> 文本" 这类写法（多余层级被压平）。
            // 用 Border 只设左边框 + 左圆角，模拟设计稿里「一条竖线挂着引用块」的样式。
            if (trimmed.StartsWith(">"))
            {
                var content = trimmed.TrimStart('>').Trim();
                var quote = new Border
                {
                    BorderBrush = Violet,
                    BorderThickness = new Thickness(2, 0, 0, 0),
                    Background = new SolidColorBrush(Color.Parse("#A78BFA"), 0.08),
                    CornerRadius = new CornerRadius(0, 6, 6, 0),
                    Padding = new Thickness(14, 9, 14, 9),
                    Margin = new Thickness(0, 0, 0, 18),
                    Child = InlineText(content, 12.5, Muted)
                };
                _root.Children.Add(quote);
                i++;
                continue;
            }

            // 列表
            // 仅支持 - 与 * 两种无序标记；用 Grid("16,*") 让圆点与文字分列，文字换行时保持悬挂缩进。
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                var row = new Grid
                {
                    Margin = new Thickness(0, 0, 0, 7),
                    ColumnDefinitions = new ColumnDefinitions("16,*")
                };
                var dot = new Ellipse
                {
                    Width = 5,
                    Height = 5,
                    Fill = Accent,
                    Opacity = 0.75,
                    // 顶部对齐 + 9px 偏移，让圆点大致落在第一行文字的视觉中线。
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(2, 9, 0, 0)
                };
                var block = InlineText(trimmed[2..], 13, Body);
                // 文字放到第二列，第一列留给圆点；列宽固定 16 保证多行列表左边缘对齐。
                Grid.SetColumn(block, 1);
                row.Children.Add(dot);
                row.Children.Add(block);
                _root.Children.Add(row);
                i++;
                continue;
            }

            // 表格
            // 连续的 | 行整体收集后再一次性构建，才能按最大列数统一分配列宽。
            // 关键：分隔行 |---|---| 的每个单元格只由 -、: 和空格组成，据此过滤掉，
            // 否则表头下面会多出一行 "---"；同时该行不参与 rows.Count，避免表头判断错位。
            if (trimmed.StartsWith("|"))
            {
                var rows = new List<string[]>();
                while (i < lines.Length && lines[i].TrimStart().StartsWith("|"))
                {
                    // Trim('|') 去掉首尾竖线，再按 | 切分并逐格 Trim，得到纯文本单元格。
                    var cells = lines[i].Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                    if (!cells.All(c => c.All(ch => ch == '-' || ch == ':' || ch == ' ')))
                        rows.Add(cells);
                    i++;
                }
                if (rows.Count > 0) _root.Children.Add(Table(rows));
                continue;
            }

            // 兜底：普通段落。没有匹配任何语法的行都走这里，
            // 因此不支持的 Markdown 语法不会报错，只是被当作纯文本原样显示。
            _root.Children.Add(new Border
            {
                Margin = new Thickness(0, 0, 0, 12),
                Child = InlineText(trimmed, 13, Body)
            });
            i++;
        }
    }

    /// <summary>
    /// 生成标题控件：withBar 为 true 时在文字左侧加一条 3px 的紫蓝渐变竖条（二级标题用）。
    /// </summary>
    private static Control Heading(string text, double size, IBrush brush, bool withBar)
    {
        var content = InlineText(text, size, brush, FontWeight.SemiBold);
        if (!withBar)
        {
            // 无竖条时直接返回文本，仅补上下外边距，少一层容器。
            content.Margin = new Thickness(0, 14, 0, 8);
            return content;
        }

        // 有竖条时用 Grid("Auto,*")：左列自适应竖条宽度，右列吃掉剩余空间放标题。
        var grid = new Grid
        {
            Margin = new Thickness(0, 18, 0, 10),
            ColumnDefinitions = new ColumnDefinitions("Auto,*")
        };
        var bar = new Border
        {
            Width = 3,
            Height = 14,
            CornerRadius = new CornerRadius(2),
            // 竖向渐变从主色过渡到紫色，让装饰条与品牌色呼应。
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#6C8BFF"), 0),
                    new GradientStop(Color.Parse("#A78BFA"), 1)
                }
            },
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(content, 1);
        grid.Children.Add(bar);
        grid.Children.Add(content);
        return grid;
    }

    /// <summary>
    /// 生成代码块：深底圆角容器 + 等宽字体；NoWrap 保留原始缩进与对齐，
    /// 代价是超长行不会折行，配合外层禁用的横向滚动条会被直接裁掉。
    /// </summary>
    private static Control CodeBlock(string code)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0B0D12")),
            // 1px 白色 6.5% 的描边：在深色页面里勾出代码块的边界，又不至于像输入框。
            BorderBrush = new SolidColorBrush(Colors.White, 0.065),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(15, 13, 15, 13),
            Margin = new Thickness(0, 4, 0, 10),
            Child = new TextBlock
            {
                Text = code,
                FontFamily = Mono,
                // 行高 20 比字号 11.5 大不少，保证代码行之间呼吸感充足。
                FontSize = 11.5,
                LineHeight = 20,
                Foreground = new SolidColorBrush(Color.Parse("#B9C1D4")),
                TextWrapping = TextWrapping.NoWrap
            }
        };
    }

    /// <summary>
    /// 生成表格：先按所有行的最大列数建立等宽列，再逐行逐格放入 Border（仅保留底边线作分隔）。
    /// </summary>
    private static Control Table(IReadOnlyList<string[]> rows)
    {
        var grid = new Grid
        {
            Margin = new Thickness(0, 4, 0, 16)
        };
        // 列数取所有行的最大值：即使某行少写了单元格，也不会让表格塌陷；缺的格子后面补空串。
        var cols = rows.Max(r => r.Length);
        // 统一用 Star 均分：不测量内容宽度，宽表格在窄窗口下也能整体收缩而不是溢出。
        for (var c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        for (var r = 0; r < rows.Count; r++)
        {
            // 行高 Auto：由单元格内容决定，避免固定高度导致长文本被裁。
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            // 第一行视为表头：大写 + 加粗 + 更亮的分隔线，其余行用正文样式。
            var isHead = r == 0;
            for (var c = 0; c < cols; c++)
            {
                var cell = c < rows[r].Length ? rows[r][c] : string.Empty;
                Control content = isHead
                    ? new TextBlock
                    {
                        // 表头转大写是设计稿的排版约定，中文字符不受影响。
                        Text = cell.ToUpperInvariant(),
                        FontSize = 11,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#9BA3B7"))
                    }
                    : InlineText(cell, 12, new SolidColorBrush(Color.Parse("#B0B8CB")));

                var border = new Border
                {
                    // 表头上下内边距 7、正文 8，视觉上让表头略紧凑一些。
                    Padding = new Thickness(10, isHead ? 7 : 8, 10, isHead ? 7 : 8),
                    // 只画底边：表头 12% 白、正文 4% 白，形成主次分明的横向分隔线。
                    BorderBrush = isHead
                        ? new SolidColorBrush(Colors.White, 0.12)
                        : new SolidColorBrush(Colors.White, 0.04),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Child = content
                };
                Grid.SetRow(border, r);
                Grid.SetColumn(border, c);
                grid.Children.Add(border);
            }
        }
        return grid;
    }

    /// <summary>解析行内 **加粗** 与 `代码`。</summary>
    // 做法：单次扫描字符，普通字符先写进 buffer，遇到标记再 Flush 成 Run；
    // 这样一段文本会被拆成若干 Run（普通 / 加粗 / 等宽代码），由同一个 TextBlock 负责排版换行。
    private static TextBlock InlineText(string text, double size, IBrush brush,
        FontWeight weight = FontWeight.Normal)
    {
        var inlines = new InlineCollection();
        var tb = new TextBlock
        {
            FontSize = size,
            Foreground = brush,
            FontWeight = weight,
            // 行高 1.75 倍：中文正文加行距后更易读，也接近设计稿的行间留白。
            LineHeight = size * 1.75,
            TextWrapping = TextWrapping.Wrap
        };

        var buffer = new System.Text.StringBuilder();

        // Flush 把缓冲区里累积的普通文本落成一个 Run；空缓冲区直接跳过，避免产生空 Run 影响排版。
        // 每次遇到样式切换前都必须先 Flush，否则加粗前的文字会被错误地并入加粗片段。
        void Flush()
        {
            if (buffer.Length == 0) return;
            inlines.Add(new Run { Text = buffer.ToString() });
            buffer.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            // 加粗：需要连续两个 *。先找配对的结束 **，找不到就当普通字符处理（单个 * 原样输出）。
            if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
            {
                var end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > 0)
                {
                    Flush();
                    inlines.Add(new Run
                    {
                        // 内容取 i+2..end（去掉两侧的 **），并换成更亮的颜色强调。
                        Text = text[(i + 2)..end],
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#E8EAEF"))
                    });
                    // 跳到结束标记的第二个 *，循环的 i++ 会把它带到 end+2，正好越过结束符。
                    i = end + 1;
                    continue;
                }
            }

            // 行内代码：单个反引号成对匹配，改等宽字体并略微缩小字号，让代码与正文基线更协调。
            if (text[i] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    Flush();
                    inlines.Add(new Run
                    {
                        Text = text[(i + 1)..end],
                        FontFamily = Mono,
                        // 字号 -1.5：等宽字体视觉上偏大，缩小后才能与周围中文正文齐平。
                        FontSize = size - 1.5,
                        Foreground = CodeBrush
                    });
                    // 这里只跳到结束反引号本身，循环 i++ 后正好从它后面继续。
                    i = end;
                    continue;
                }
            }

            // 普通字符入缓冲，等下一次样式切换或循环结束时统一 Flush。
            buffer.Append(text[i]);
        }
        // 收尾：把最后一段普通文本落成 Run，否则行尾内容会丢失。
        Flush();
        tb.Inlines = inlines;
        return tb;
    }
}
