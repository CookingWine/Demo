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
    //
    // 补充一句大实话：对这个控件来说 AffectsRender 基本是多余的。
    // ScrollViewer 本身没有重写 Render 来画内容（内容是一个真实的子控件 StackPanel），
    // 所以 Markdown 变化时触发的 InvalidateVisual 只会让 ScrollViewer 重绘一次背景与滚动条，
    // 并不会让新内容「画出来」—— 真正让界面更新的是 Build() 改 _root.Children 引发的布局。
    // 保留它无害（多一次几乎空转的重绘），删掉也不影响功能。
    static MarkdownPreview()
    {
        AffectsRender<MarkdownPreview>(MarkdownProperty);
    }

    public MarkdownPreview()
    {
        // 直接把 StackPanel 设为 Content，避免再包一层 Grid/UserControl。
        // _root 是字段初始化器 new 出来的，此时已经存在；后续 Build 只往里加子元素，不换实例。
        Content = _root;
        // 禁用横向滚动：表格与代码块自行处理换行，若允许横向滚动会与纵向滚动条争抢空间。
        // Disabled 与 Hidden 的区别：Hidden 只是不显示滚动条但仍可滚动，
        // Disabled 是彻底不参与滚动 —— 这里要的是后者，因为代码块的 NoWrap 长行要被直接裁掉。
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        // Auto：内容超过可视区域才出现纵向滚动条；内容少时无缝贴合，不会留一条空槽。
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        // 内边距刻意做成不对称：底部留 60 让最后一行不贴住窗口下沿。
        // 左 26 / 上 24 / 右 28：左右不相等是有意的（左侧略窄），
        // 因为滚动条在右侧会占掉约 8~12px，视觉上把右侧「吃掉」一部分，靠多一点内边距拉回来。
        // 这 60 不是随便留的：它约等于 2.5 行正文（行高 22.75），保证滚到底时最后一段还有呼吸空间。
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
    // 任意 StyledProperty 变更都会走到这里（Padding、Foreground、宽高……全都会），
    // 所以必须先 base.OnPropertyChanged，再用 change.Property 精确过滤，只对 Markdown 做重活。
    // 时序上：属性变更 -> 本回调（同步）-> Build 改子控件集合 -> 布局系统标记失效 ->
    //           下一帧 Measure/Arrange -> 界面更新。
    // 也就是说 Build 不会立刻触发排版，只是把「需要重新布局」这件事登记下来。
    // 重复调用是安全的：Build 第一步就 Clear，结果只取决于当前的 Markdown 值（幂等）。
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty) Build();
    }

    /// <summary>
    /// 首次挂到可视树时补一次 Build：属性可能在控件构造前就已通过 XAML 赋值，
    /// 那时 OnPropertyChanged 已经执行过，但布局尚未开始；这里确保最终内容一定是最新的。
    /// </summary>
    // 这个方法容易看成「多余的一次重建」，实际上它兜的是「解析时机」而不是「解析结果」。
    // 先把两个时序摆出来：
    //
    //   情形 1：Markdown 在挂树之前就已有值（XAML 里写字面量、或 DataContext 在父级已就绪）。
    //     属性赋值发生在构造之后、挂树之前，OnPropertyChanged 已经 Build 过一次，
    //     控件树是完整的。挂树时这次 Build 属于重复劳动，
    //     结果与之前完全相同（Build 是幂等的：先 Clear 再重建），只是多花一次重建的开销。
    //
    //   情形 2：Markdown 的值依赖「挂树之后才具备的条件」。
    //     典型是三种：值来自继承的 DataContext（DataContext 沿逻辑树向下传递，
    //     可能在本控件挂树那一帧才传到）；值来自样式 setter（样式在挂树时应用）；
    //     值来自模板绑定。这三种情况下，挂树之前的 OnPropertyChanged 根本没被触发过，
    //     控件树还是空的 —— 而挂树时正是这些值刚就位的时刻。
    //
    // 所以准确的说法是：OnAttachedToVisualTree 里的这次 Build 不是「保证内容最新」，
    // 而是「保证挂树瞬间就按当时的值渲染一次」，避免出现「值已就位但界面空白」的一帧。
    // 之后若值再次变化，仍然靠 OnPropertyChanged 走正常路径。
    //
    // 安全性：此时代码只改 _root.Children，尚未进行第一次 Measure，不存在「先布局再改树」的抖动。
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
    // 调用者：OnPropertyChanged（Markdown 变化）与 OnAttachedToVisualTree（挂树兜底）。
    // 前置状态：Markdown 可能为 null / 空串 / 只有空白；_root 一定已构造（字段初始化器保证）。
    // 后置状态：_root.Children 被整棵重建；StackPanel 会因集合变更事件自动重新测量与排版，
    //           因此这里不需要（也没有）任何属性通知 —— 界面靠 ObservableCollection 式的变更事件刷新。
    // 成本：文档几百行时，一次 Build 会创建几百个控件；相比一次完整布局仍然便宜，
    //       所以文档里没有做「差异更新」，直接全量重建。
    //
    // ===== 状态机的骨架 =====
    // 外层 while 每次处理「一行」，处理完必须让 i 严格增大，否则就是死循环。
    // 各分支对 i 的推进量（这是读这段代码时最该记的一张表）：
    //   空行        -> i += 1（消费这一行）
    //   代码块      -> 先 i += 1 跳过起始围栏，内层 while 每收一行 i += 1，
    //                  结束后再 i += 1 跳过结束围栏；总计 = 1 + 内容行数 + 1
    //   标题 #/##/### -> i += 1
    //   引用 >       -> i += 1
    //   列表 -/*     -> i += 1
    //   表格 |       -> 内层 while 一口气吃掉「所有连续的 | 行」，退出时 i 停在第一条非表格行上，
    //                  外层不再额外 i++，所以总计 = 表格行数
    //   兜底段落     -> i += 1
    // 除了代码块与表格，其余分支都恰好推进 1 行 —— 这是「不会死循环」的直接保证。
    // 代码块与表格用内层 while 推进，内层循环的条件里带 i < lines.Length，也会在末尾自然收敛。
    private void Build()
    {
        // 步骤 1：整棵树先清空：不做增量 diff，避免旧内容残留（例如从长文档切到短文档）。
        // 用 Clear 而不是给 Content 换一个新的 StackPanel：换实例会让 ScrollViewer 的 Content
        // 指向新对象，滚动位置、焦点、已挂载的子控件全部重来，反而更贵。
        _root.Children.Clear();
        // 步骤 2：取文本。空白（含全空格、全换行）直接返回，此时 _root 是空 StackPanel，
        // 界面上就是一整块空白内容区，滚动条也不会出现。
        var text = Markdown;
        if (string.IsNullOrWhiteSpace(text)) return;

        // 步骤 3：统一换行符再切分。
        // 统一换行符再切分：Windows 的 \r\n 与 Unix 的 \n 走同一套逻辑，避免行尾残留 \r 影响 StartsWith 判断。
        //   若只按 '\n' 切而不先替换，Windows 文本每行末尾会留一个 \r：
        //   TrimEnd() 能去掉它（\r 属于空白字符），所以本例其实不会出错；
        //   但 StartsWith 的判断发生在 TrimEnd 之后的 trimmed 上，安全。
        //   真正会出问题的是 \r\n 被当成两行（旧式 Mac 的 \r 单独换行）—— 这里没有处理，属于已知限制。
        //   Split('\n') 保留空行（不会自动丢弃），所以文档中的空行会变成空字符串元素，由下面的分支处理。
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var i = 0;

        // 步骤 4：主循环。i 是「当前待处理行」的索引，所有分支都必须在 continue 前推进它。
        while (i < lines.Length)
        {
            var line = lines[i];
            // 只去尾部空白而保留行首缩进：行首空格在 Markdown 里可能有语义（虽然本实现未处理嵌套）。
            // 注意 trimmed 只 TrimEnd：判断 "### " 这类前缀时，行首的一个空格就会让匹配失败，
            // 于是该行落入兜底段落分支。真正的 Markdown 允许最多 3 个空格的缩进，本实现不支持。
            var trimmed = line.TrimEnd();

            // 空行：用一个 10px 高的空 Border 充当段间距，比给每个块加 margin 更容易统一控制。
            // 为什么用「空 Border」而不是直接跳过：跳过就没有段间距了；
            // 而给每个块加 margin 需要处理「块与块之间双倍间距」的问题（相邻两个都有下边距会叠加）。
            // 用一条独立的高度占位，间距数量就等于空行数量，语义最直白。
            // 10 是设计稿的段间距：连续两个空行 = 20px，三个 = 30px，不做上限收敛。
            if (trimmed.Length == 0)
            {
                _root.Children.Add(new Border { Height = 10 });
                // 唯一的推进点：这个分支必须 i++ 然后 continue，否则会无限添加 10px 空条。
                i++;
                continue;
            }

            // 代码块
            // 遇到 ``` 开始收集，直到下一个以 ``` 开头的行（语言标识直接忽略）；
            // 内层 while 结束时 i 停在结束围栏上，再 i++ 跳过它，避免把围栏当成普通段落。
            //
            // 三条容易忽略的细节：
            //   1) 起始围栏那一行的内容整体丢弃，包括 ```csharp 这种语言标识 —— 本实现不做语法高亮，
            //      留着也没用，所以没有保存。
            //   2) 内层 while 里对内容行用的是 TrimStart().StartsWith("```")：
            //      允许结束围栏前面有缩进（缩进的 ``` 也算结束），但内容行本身是原样保存的。
            //   3) 没有结束围栏（文档写到一半）时，内层 while 会一直收到文件末尾，
            //      i 变成 lines.Length，后面那句 i++ 再把它变成 Length+1，
            //      外层 while 条件 i < lines.Length 立刻失败 —— 效果是「剩余全文都当成代码块」，
            //      不报错、不丢失内容，属于可接受的降级。
            if (trimmed.StartsWith("```"))
            {
                var code = new List<string>();
                // 跳过起始围栏（这是本分支的第一次推进）
                i++;
                // 内层循环是「吞多行」的主角：每收一行内容推进一次 i，直到遇到结束围栏或文件末尾。
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                {
                    code.Add(lines[i]);
                    i++;
                }
                // 跳过结束围栏。若上面是因为「到文件末尾」退出的，这里的 i++ 会让 i 变成 Length+1，
                // 外层循环随即结束 —— 不需要额外判断，是这段代码能保持简短的原因。
                i++;
                // 用换行符重新拼回多行文本：代码行按原样收集，保留缩进与空行，只丢掉首尾两条围栏。
                // 这里必须用 "\n" 而不是 Environment.NewLine：TextBlock 对两种换行都能正确断行，
                // 用固定字符可以避免同一份文档在不同平台上产生不同的字符串。
                _root.Children.Add(CodeBlock(string.Join("\n", code)));
                continue;
            }

            // 标题
            // 先判断 ### 再判断 ## 再判断 #：否则 "### x" 会被 "## " 前缀命中，标题层级错位。
            // 这是「前缀判定」类解析器的经典陷阱：短前缀是长前缀的子串，必须从长到短试。
            //
            // 三个分支的切片下标与前缀长度严格对应：
            //   "### " 是 4 个字符（三个 # 加一个空格）-> trimmed[4..] 取到正文
            //   "## "  是 3 个字符                  -> trimmed[3..]
            //   "# "   是 2 个字符                  -> trimmed[2..]
            //   若少切一个字符，标题会以「#」或空格开头；若多切一个，正文首字被吃掉。
            // 注意 StartsWith 里都带了尾部空格：这是为了区分「标题」和「以 # 开头的普通文本」
            // （例如 C# 这样的字符串 #region 不会被误判成标题）。
            //
            // 三级标题的字号 14.5 / 16 / 24 是设计稿的字阶，对应 h3 / h2 / h1；
            // 只有 h2 带左侧装饰竖条（withBar = true），h1 用整条分隔线，h3 只靠字号区分。
            if (trimmed.StartsWith("### "))
            {
                _root.Children.Add(Heading(trimmed[4..], 14.5, Text0, false));
                i++;
                continue;
            }
            if (trimmed.StartsWith("## "))
            {
                // 二级标题用一个比正文更亮的颜色 #E4E8F2（Text0 是 #F2F4FA，最亮的一档）；
                // 这里没有复用 Text0 而是就地 new 一个画刷，是因为这一档颜色只有 h2 用到。
                _root.Children.Add(Heading(trimmed[3..], 16, new SolidColorBrush(Color.Parse("#E4E8F2")), true));
                i++;
                continue;
            }
            if (trimmed.StartsWith("# "))
            {
                // 一级标题额外带一条细分隔线，用 StackPanel 竖排文字与线条。
                // Spacing = 14 是「标题文字」与「分隔线」之间的间距，
                // Margin 的 6 是整块标题与下一块内容之间的额外距离（再叠加下一个块的 Margin）。
                var h1 = new StackPanel { Spacing = 14, Margin = new Thickness(0, 0, 0, 6) };
                // 字号 24 是整个文档里最大的字，SemiBold 而不是 Bold：
                // 24px 的中文用 Bold 会显得发糊，半粗既有分量又保住字形。
                h1.Children.Add(InlineText(trimmed[2..], 24, Text0, FontWeight.SemiBold));
                // 分隔线是一条高 1px、白色 6.5% 的 Border：
                // 深色底上纯白 100% 会很刺眼，6.5% 刚好是一道「若隐若现」的细线，与表格分隔线同一档。
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
            //
            // TrimStart('>') 用的是 char 重载：它会一次去掉开头连续的所有 '>'，
            // 所以 ">>> 文本" 也会被压成 "文本"，嵌套层级信息直接丢掉（本实现不画多级引用）。
            // 末尾的 .Trim() 负责去掉 ">" 与正文之间的空格。
            //
            // 几个尺寸的来历：
            //   BorderThickness(2,0,0,0) 只保留左边 2px 的竖线，这是引用的视觉标识；
            //   CornerRadius(0,6,6,0)    左边保持直角（贴着竖线），右侧两个角圆 6px；
            //   Background 紫色 8%       深底上极淡的一层底色，用于和正文区分，不做强调；
            //   Padding(14,9,14,9)       左右 14 让正文与竖线拉开距离；
            //   Margin 底部 18           比普通段落的 12 更大 —— 引用块本身有底色，
            //                            与下一块贴太近会看成同一个区域。
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
                    // 引用正文用 Muted（#A9B1C6）而不是 Body（#BFC7D9）：引用是次要信息，压暗一档。
                    Child = InlineText(content, 12.5, Muted)
                };
                _root.Children.Add(quote);
                i++;
                continue;
            }

            // 列表
            // 仅支持 - 与 * 两种无序标记；用 Grid("16,*") 让圆点与文字分列，文字换行时保持悬挂缩进。
            //
            // 为什么必须用两列 Grid 而不是「圆点 + 空格拼接」：
            //   拼字符串的话，第二行文字会顶到最左边，和圆点对齐，视觉上就分不清哪行属于哪一项；
            //   分两列之后，文字整块都在第二列，换行时自然形成「悬挂缩进」。
            // 注意：有序列表（1. / 2.）不在支持范围内，会走兜底分支变成普通段落。
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                var row = new Grid
                {
                    // 每项底部 7px：列表项之间比段落（12）更紧凑，读起来才像一个整体。
                    Margin = new Thickness(0, 0, 0, 7),
                    // 第一列固定 16：圆点的槽位宽度，不用 Auto 是为了让所有列表项的左边缘严格对齐。
                    ColumnDefinitions = new ColumnDefinitions("16,*")
                };
                var dot = new Ellipse
                {
                    Width = 5,
                    Height = 5,
                    Fill = Accent,
                    // 75% 不透明度：纯主色的 5px 圆点在深底上偏亮，会抢正文的注意力。
                    Opacity = 0.75,
                    // 顶部对齐 + 9px 偏移，让圆点大致落在第一行文字的视觉中线。
                    // 9 的算法：正文 13px、行高 13*1.75 ≈ 22.75，第一行中线约在 11.4；
                    // 圆点直径 5，要让它居中需要上边距 11.4 - 2.5 ≈ 8.9，取整为 9。
                    // 左 2 是微调：第一列宽 16，圆点只占 5，留 2 让圆点略靠左，与正文的视觉边距更协调。
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(2, 9, 0, 0)
                };
                // trimmed[2..] 跳过 "- " 这两个字符；列表项不解析嵌套，缩进会被前面的 TrimEnd 保留但不起作用。
                var block = InlineText(trimmed[2..], 13, Body);
                // 文字放到第二列，第一列留给圆点；列宽固定 16 保证多行列表左边缘对齐。
                // Grid.SetColumn 是附加属性，必须在加入 Children 之前或之后设置都能生效，
                // 但一定要设，否则文字和圆点会叠在第 0 列。
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
            //
            // 这个分支是第二个「吞多行」的地方，推进规则与代码块不同：
            //   内层 while 每收一行就 i++，退出时 i 已经指向「第一条不是表格的行」，
            //   所以分支末尾没有 i++，直接 continue —— 这一点和代码块正好相反
            //   （代码块是 i 停在结束围栏上，需要再 i++ 跨过它）。
            //   漏写或误加一次 i++ 都会造成「吞掉下一行」或「把表格行再解析一遍」。
            //
            // 一处不一致（如实记录）：进入本分支的判断用 trimmed（只去掉尾部空白），
            // 所以第一行必须顶格写 |；而内层循环的条件用 lines[i].TrimStart()，
            // 允许后续行带前导空格。结果是「整张表格第一行缩进、后面不缩进」时，
            // 第一行不会进入表格分支（变成段落），而它后面的行也不会被收集 —— 表格整体失效。
            if (trimmed.StartsWith("|"))
            {
                var rows = new List<string[]>();
                while (i < lines.Length && lines[i].TrimStart().StartsWith("|"))
                {
                    // Trim('|') 去掉首尾竖线，再按 | 切分并逐格 Trim，得到纯文本单元格。
                    // 用 Trim('|') 而不是 TrimStart/TrimEnd 分开写：它会把首尾所有 | 都去掉，
                    // 因此 "|a|b|" 与 "|a|b" 都能得到两格；代价是末尾的空单元格（"|a|b||"）会被吞掉。
                    var cells = lines[i].Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                    // 分隔行判定：所有单元格的所有字符都只能是 - : 空格 三者之一。
                    //   · 三个空格的情况（"| | |"）也会被判成分隔行 —— 空行被顺手过滤，副作用无害；
                    //   · 注意 All 对空字符串返回 true，所以 "||" 这种行同样会被过滤掉。
                    if (!cells.All(c => c.All(ch => ch == '-' || ch == ':' || ch == ' ')))
                        rows.Add(cells);
                    // 无论这一行是数据行还是分隔行，都要推进 i，否则内层循环死锁。
                    i++;
                }
                // 全部行都是分隔行时 rows 为空，此时不加任何控件 —— 避免出现一个 0 行 0 列的空白 Grid。
                if (rows.Count > 0) _root.Children.Add(Table(rows));
                continue;
            }

            // 兜底：普通段落。没有匹配任何语法的行都走这里，
            // 因此不支持的 Markdown 语法不会报错，只是被当作纯文本原样显示。
            //
            // 两点行为需要知道：
            //   1) 不做「相邻行合并成一段」：Markdown 规范里连续两行普通文本属于同一段落，
            //      本实现是「一行一控件」，于是每行之间都会多出 12px 间距。
            //      文档若按 80 列硬换行，读起来会比标准 Markdown 松散。
            //   2) 外面套一层 Border 只是为了统一承载 Margin：
            //      TextBlock 自己也有 Margin，但行内 Run 的排版在个别字体下会被外层的
            //      Margin 影响基线，用 Border 包一层更稳妥，也让所有块级元素的结构保持一致
            //      （都是「容器 + Child」）。
            _root.Children.Add(new Border
            {
                // 底部 12 是正文段间距，比列表项的 7 大、比引用的 18 小，正好在中间一档。
                Margin = new Thickness(0, 0, 0, 12),
                Child = InlineText(trimmed, 13, Body)
            });
            // 本分支的最后一次推进。到这里所有分支都有推进，while 必然收敛。
            i++;
        }
    }

    /// <summary>
    /// 生成标题控件：withBar 为 true 时在文字左侧加一条 3px 的紫蓝渐变竖条（二级标题用）。
    /// </summary>
    // 调用者：Build 的「### 」分支（withBar = false）与「## 」分支（withBar = true）。
    //   「# 」一级标题不走这里，它在 Build 里单独组装（文字 + 整条分隔线）。
    // 前置状态：text 是已去掉前缀的标题正文；size / brush 由调用方按层级给定。
    // 后置状态：返回一个 Control（可能直接是 TextBlock，也可能是 Grid），不修改外部状态。
    // 上下边距的两个档位：无竖条 14/8，有竖条 18/10 —— 有竖条时块本身更高，需要更多上留白。
    private static Control Heading(string text, double size, IBrush brush, bool withBar)
    {
        // 标题统一 SemiBold：InlineText 的默认字重是 Normal，这里显式传进去。
        // 注意标题内部仍然解析行内 ** 与 `，所以标题里可以混排加粗。
        var content = InlineText(text, size, brush, FontWeight.SemiBold);
        if (!withBar)
        {
            // 无竖条时直接返回文本，仅补上下外边距，少一层容器。
            // 上 14 / 下 8：上边距大于下边距，让标题「远离上文、贴近下文」，
            // 这是排版里的常见做法 —— 标题属于它下面的内容，而不是它上面的。
            content.Margin = new Thickness(0, 14, 0, 8);
            return content;
        }

        // 有竖条时用 Grid("Auto,*")：左列自适应竖条宽度，右列吃掉剩余空间放标题。
        //
        // Auto 与 * 的取舍在这里很具体：
        //   左列 Auto —— 宽度由竖条自己决定（Width 3 + 右侧 Margin 8 = 11），不写死 11，
        //     将来把竖条加粗或改间距时不用同步改列定义；
        //   右列 *    —— 吃掉剩余全部宽度，标题文字才有空间换行。
        //     若两列都写 Auto，Grid 会按内容量宽，长标题会一路撑出控件；
        //     若右列写固定宽度，窗口变宽时标题右边会留一大片空白。
        var grid = new Grid
        {
            Margin = new Thickness(0, 18, 0, 10),
            ColumnDefinitions = new ColumnDefinitions("Auto,*")
        };
        var bar = new Border
        {
            // 宽 3、高 14：3px 在深色底上刚好是一条清晰的线；
            // 高 14 比标题字号 16 略矮，视觉上「短于字高」，看起来像强调符而不是边框。
            Width = 3,
            Height = 14,
            // 圆角 2：3px 宽的图形圆角 2 已接近半圆，两端是圆头，不是硬切口。
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
    // 调用者：Build 的代码块分支，传入已经去掉首尾围栏、用 \n 拼好的纯文本。
    // 前置状态：code 可以是空串（``` 紧跟 ``` 的空代码块），此时仍会生成一个高约 46px 的空框。
    // 后置状态：返回一个 Border，不修改外部状态。
    // 几个数值的来历：
    //   Background #0B0D12  比页面底色还深一档，让代码块「凹下去」；
    //   BorderBrush 白 6.5% 与 h1 分隔线、表格表头线同一档，保证整体线感统一；
    //   CornerRadius 8      比卡片（12~16）小，比按钮（6）大，符合「内容块」的层级；
    //   Padding 15/13       比正文段落的缩进略小，避免在窄窗口里挤压代码行；
    //   LineHeight 20       字号 11.5 的 1.74 倍，与正文的 1.75 倍保持一致的行间呼吸。
    // 已知限制：不做语法高亮、不做语言标识、不支持横向滚动，长行会被右侧裁掉。
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
    // 调用者：Build() 的表格分支，传入的是已经过滤掉分隔行的二维字符串。
    // 前置状态：rows 非空（Build 里用 rows.Count > 0 保证过），但各行的长度可能不一致。
    // 后置状态：返回一个 Grid；Grid 只做布局，不参与滚动，宽度跟随 StackPanel 的内容宽度。
    //
    // ===== 为什么用 Grid + 每列 1*，而不是 ColumnDefinitions 写死宽度 =====
    //   · Grid 的 Star 分配规则：先量完 Auto 与固定像素列，把「剩余可用宽度」按 1:1:1 分给各星列。
    //     于是 n 个 1* 列每列正好拿到 可用宽度/n，表格总宽恒等于父容器宽度 —— 不多不少。
    //   · 写死宽度（例如每列 120）的问题：3 列就要 360px，窗口缩到 300 时表格右半边溢出；
    //     而本控件的 HorizontalScrollBarVisibility 是 Disabled（见构造函数），溢出部分会被直接裁掉，
    //     用户看不到最右一列，也没有任何滚动条提示。
    //   · 用 Auto 的问题：Auto 会按内容量宽，长文本（尤其是没有空格的中文/长 URL）会把列撑得很宽，
    //     同样导致溢出；而且同一列在不同行上宽度不一致时，Grid 取最大值，整表会变得很不稳定。
    //   · 1* 的代价：完全不看内容长度，一个只有「序号」的窄列和一个长描述列会分到同样宽度。
    //     这是设计上接受的取舍（文件头也写了）：宁可等宽、可预测、不溢出。
    //
    //   · 内容过长时不会溢出，而是换行：单元格里的文字走 InlineText，它把 TextWrapping 设成 Wrap。
    //     所以窄窗口下表格会「变高」而不是「变宽」。
    private static Control Table(IReadOnlyList<string[]> rows)
    {
        var grid = new Grid
        {
            // 上 4 / 下 16：上边距小是为了让表格贴近它上面那行文字，下边距大是为了与下一段分隔开。
            Margin = new Thickness(0, 4, 0, 16)
        };
        // 列数取所有行的最大值：即使某行少写了单元格，也不会让表格塌陷；缺的格子后面补空串。
        // rows.Max(r => r.Length) 走的是 LINQ：对空集合会抛异常，所以 Build 里必须先判 Count。
        var cols = rows.Max(r => r.Length);
        // 统一用 Star 均分：不测量内容宽度，宽表格在窄窗口下也能整体收缩而不是溢出。
        // ColumnDefinition(1, GridUnitType.Star) 的参数是「权重 1」+「星号单位」，
        // 权重都相同，所以各列等宽；若把某个列改成 2 星，它会拿到其他列的两倍宽。
        for (var c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        for (var r = 0; r < rows.Count; r++)
        {
            // 行高 Auto：由单元格内容决定，避免固定高度导致长文本被裁。
            // 注意 RowDefinitions 是在循环里逐个 Add 的，行数就等于 rows.Count；
            // 少 Add 一行的话，那一行的控件会被放进 Grid 的「隐式行」，高度不受控。
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            // 第一行视为表头：大写 + 加粗 + 更亮的分隔线，其余行用正文样式。
            // 这里的 r 是整个 rows 的下标，分隔行已经在 Build 里被过滤掉，
            // 所以 0 一定是表头（这也是 Build 里必须过滤而不是「跳过渲染」的原因）。
            var isHead = r == 0;
            for (var c = 0; c < cols; c++)
            {
                // 缺格补空串：某行只写了 2 格而表格有 3 列时，第 3 格放空字符串，
                // 保证该行仍然占满 3 列（否则底边线只画到第 2 列，表格看起来缺一角）。
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
                    // 左右都是 10，保证同一列的内容左边缘严格对齐（否则表头与正文会错开）。
                    Padding = new Thickness(10, isHead ? 7 : 8, 10, isHead ? 7 : 8),
                    // 只画底边：表头 12% 白、正文 4% 白，形成主次分明的横向分隔线。
                    // 12% 与 4% 差三倍，远看能分出「表头区」和「数据区」，又不至于像网格线那样抢眼。
                    BorderBrush = isHead
                        ? new SolidColorBrush(Colors.White, 0.12)
                        : new SolidColorBrush(Colors.White, 0.04),
                    // 四边厚度里只有底边是 1，其余为 0：这就是「只保留横线」的实现方式。
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Child = content
                };
                // 定位到对应的行列。Grid 的行列是附加属性，必须在 Add 之前设置好，
                // 否则会先按 (0,0) 参与一次测量，再挪位置，引起一次多余的布局。
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
    //
    // 调用者：Build 里所有块级分支（标题、引用、列表项、表格单元格、兜底段落）都走这里，
    //         所以「行内样式」在全文档是一致的。
    // 前置状态：text 是一行的原始文本（已去尾部空白），可能含 ** 与 ` 标记，也可能一个都没有。
    // 后置状态：返回一个新的 TextBlock，其 Inlines 里是若干 Run；不修改任何外部状态。
    //
    // 为什么用「一个 TextBlock + 多个 Run」而不是「多个 TextBlock 拼起来」：
    //   换行与对齐必须由同一个 TextBlock 负责。切成多个 TextBlock 后，
    //   每个都会独立换行，中英文混排时行距、基线都会对不齐。
    //   Run 是 TextBlock 内部的文本片段，只影响「这一段字符」的样式，不影响整体排版。
    // 未支持：链接、斜体、删除线、转义字符（\* 会被当作普通字符，但 ** 仍然生效）。
    private static TextBlock InlineText(string text, double size, IBrush brush,
        FontWeight weight = FontWeight.Normal)
    {
        var inlines = new InlineCollection();
        var tb = new TextBlock
        {
            // 这三个属性作为「默认样式」，会被单个 Run 上的同名属性覆盖：
            // 加粗 Run 覆盖 FontWeight 与 Foreground，代码 Run 覆盖 FontFamily / FontSize / Foreground。
            FontSize = size,
            Foreground = brush,
            FontWeight = weight,
            // 行高 1.75 倍：中文正文加行距后更易读，也接近设计稿的行间留白。
            // 13px 正文 -> 22.75px 行高；标题 24px -> 42px 行高。
            LineHeight = size * 1.75,
            // Wrap 是表格「窄窗口下变高而不是变宽」的前提（见 Table 里的说明）。
            TextWrapping = TextWrapping.Wrap
        };

        var buffer = new System.Text.StringBuilder();

        // Flush 把缓冲区里累积的普通文本落成一个 Run；空缓冲区直接跳过，避免产生空 Run 影响排版。
        // 每次遇到样式切换前都必须先 Flush，否则加粗前的文字会被错误地并入加粗片段。
        //
        // Flush 存在的意义（换成「不加会怎样」更好理解）：
        //   inlines 是一个有序集合，Run 加入的顺序就是屏幕上文字的顺序。
        //   buffer 里攒的是「还没决定样式的普通字符」。若遇到 ** 时直接 Add 一个加粗 Run 而不先 Flush，
        //   加粗片段就会排到缓冲区内容之前，等循环结束再统一 Flush 时，
        //   整段普通文字会被追加到最后 —— 顺序整体错乱。
        //   所以 Flush 的职责就是：在样式切换的那一刻，把「之前攒下的普通文字」按正确顺序落盘。
        //   buffer.Length == 0 的提前返回避免加入空的 Run：空 Run 虽然不显示文字，
        //   但会参与行内布局计算，可能让段首出现半像素偏移。
        //   buffer.Clear() 复用同一个 StringBuilder 实例，避免每段文字都 new 一个。
        void Flush()
        {
            if (buffer.Length == 0) return;
            inlines.Add(new Run { Text = buffer.ToString() });
            buffer.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            // 加粗：需要连续两个 *。先找配对的结束 **，找不到就当普通字符处理（单个 * 原样输出）。
            //
            // 三个判断条件各自在防什么：
            //   i + 1 < text.Length   防止 text[i+1] 越界（行尾单个 * 的情况）；
            //   text[i] == '*' && text[i+1] == '*'  确认这是一个「双星」开头；
            //   end > 0               IndexOf 找不到时返回 -1，用它判断「没配对」。
            //                         （严格说应该写 end > i，但因为搜索起点是 i+2，
            //                          一旦找到必然 >= i+2 > 0，所以 end > 0 等价。）
            // IndexOf 的起点是 i + 2 而不是 i：否则会把刚识别出的这个开头 ** 自己又匹配一遍，
            // 得到 end == i，内容区间 [i+2..i) 直接抛异常。
            // StringComparison.Ordinal 是按码位逐字比较，不走区域文化规则，
            // 避免某些文化下把 * 当作特殊情况处理（也更快）。
            if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
            {
                var end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > 0)
                {
                    Flush();
                    inlines.Add(new Run
                    {
                        // 内容取 i+2..end（去掉两侧的 **），并换成更亮的颜色强调。
                        // 这里用 SemiBold 而不是 Bold：13px 的中文用 Bold 会糊，SemiBold 更清晰。
                        // #E8EAEF 比正文的 #BFC7D9 亮一档，是「强调但不刺眼」的折中。
                        Text = text[(i + 2)..end],
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#E8EAEF"))
                    });
                    // 跳到结束标记的第二个 *，循环的 i++ 会把它带到 end+2，正好越过结束符。
                    //
                    // 逐个下标走一遍「a**b**c」（下标：0=a 1=* 2=* 3=b 4=* 5=* 6=c）：
                    //   i=1 命中双星，end = IndexOf("**", 3) = 4
                    //   正文 = text[3..4] = "b"
                    //   i = end + 1 = 5（指向第二个 *），continue 后 for 的 i++ 使 i = 6
                    //   i=6 是 'c'，正常收进 buffer —— 一个字都没漏、也没把 * 当正文。
                    // 若这里写成 i = end，i++ 后 i = 5，第二个 * 会被当作普通字符写进 buffer，
                    // 输出就变成「b*」，多出一个星号。
                    i = end + 1;
                    continue;
                }
            }

            // 行内代码：单个反引号成对匹配，改等宽字体并略微缩小字号，让代码与正文基线更协调。
            //
            // 为什么这里只找「一个」反引号，而加粗要找「两个」：
            //   Markdown 的行内代码用单反引号定界，加粗用双星定界 —— 定界符长度不同，
            //   直接决定了下面 i 的跳法不同。
            // if (end > i) 的判断：IndexOf 从 i+1 开始找，找到必然 > i，找不到返回 -1；
            //   所以这个条件同时覆盖了「找到」与「没找到」两种情况，比写 end != -1 更直白。
            if (text[i] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    Flush();
                    inlines.Add(new Run
                    {
                        // 内容取 i+1..end（去掉两侧的反引号）。
                        Text = text[(i + 1)..end],
                        FontFamily = Mono,
                        // 字号 -1.5：等宽字体视觉上偏大，缩小后才能与周围中文正文齐平。
                        // 13px 正文里的代码是 11.5px，正好和 CodeBlock 里的字号一致。
                        FontSize = size - 1.5,
                        Foreground = CodeBrush
                    });
                    // 这里只跳到结束反引号本身，循环 i++ 后正好从它后面继续。
                    //
                    // 逐个下标走一遍「a`b`c」（下标：0=a 1=` 2=b 3=` 4=c）：
                    //   i=1 命中反引号，end = IndexOf('`', 2) = 3
                    //   正文 = text[2..3] = "b"
                    //   i = end = 3（指向结束反引号），continue 后 i++ 使 i = 4
                    //   i=4 是 'c' —— 正好跳过结束反引号。
                    //
                    // ===== 为什么加粗写 i = end + 1，这里却写 i = end =====
                    //   关键差别在 end 指向的是「结束符的第几个字符」：
                    //     · 加粗的结束符是两个字符 **，end 指向第一个 *，
                    //       要让 i++ 落在 end+2（越过两个 *），就必须预先把 i 设成 end+1；
                    //     · 行内代码的结束符只有一个字符 `，end 就指向它本身，
                    //       要让 i++ 落在 end+1（越过这一个 `），预先把 i 设成 end 即可。
                    //   一句话：i = end + (结束符长度 - 1)。
                    //   两处若互换写法，加粗会多输出一个 *，代码块会少输出一个字符（跳过结束符后的一位）。
                    i = end;
                    continue;
                }
            }

            // 普通字符入缓冲，等下一次样式切换或循环结束时统一 Flush。
            // 注意这里只可能是「单字符 append」：多字符的性能问题在几百字的文档里不存在，
            // 用 StringBuilder 而不是 string += 已经足够了。
            buffer.Append(text[i]);
        }
        // 收尾：把最后一段普通文本落成 Run，否则行尾内容会丢失。
        // 这一步和循环内每次样式切换前的 Flush 是同一个函数，只是调用时机不同：
        // 循环内是「换样式了，快把之前的普通文字交出去」，这里是「遍历完了，把剩下的交出去」。
        // 漏掉这一句时，症状是「每个加粗/代码片段之后的尾巴全部消失」，
        // 而纯文本段落（没有任何标记）会整段消失 —— 因为它们从头到尾都没触发过循环内的 Flush。
        Flush();
        // 最后一次性把 Inlines 挂到 TextBlock 上，而不是边解析边 Add：
        // 后者每加一个 Run 都会触发一次行内集合的变更通知与潜在的重新排版，
        // 先在游离的 InlineCollection 上组装，最后赋一次值，只测量一次。
        tb.Inlines = inlines;
        return tb;
    }
}
