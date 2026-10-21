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
public class MarkdownPreview : ScrollViewer
{
    private readonly StackPanel _root = new() { Spacing = 0 };

    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownPreview, string?>(nameof(Markdown));

    static MarkdownPreview()
    {
        AffectsRender<MarkdownPreview>(MarkdownProperty);
    }

    public MarkdownPreview()
    {
        Content = _root;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Padding = new Thickness(26, 24, 28, 60);
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty) Build();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Build();
    }

    private static readonly IBrush Text0 = new SolidColorBrush(Color.Parse("#F2F4FA"));
    private static readonly IBrush Body = new SolidColorBrush(Color.Parse("#BFC7D9"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#A9B1C6"));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#6C8BFF"));
    private static readonly IBrush Violet = new SolidColorBrush(Color.Parse("#A78BFA"));
    private static readonly IBrush CodeBrush = new SolidColorBrush(Color.Parse("#A8BAFF"));
    private static readonly FontFamily Mono = new("JetBrains Mono, SF Mono, Consolas, Menlo, monospace");

    private void Build()
    {
        _root.Children.Clear();
        var text = Markdown;
        if (string.IsNullOrWhiteSpace(text)) return;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.TrimEnd();

            if (trimmed.Length == 0)
            {
                _root.Children.Add(new Border { Height = 10 });
                i++;
                continue;
            }

            // 代码块
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
                _root.Children.Add(CodeBlock(string.Join("\n", code)));
                continue;
            }

            // 标题
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
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(2, 9, 0, 0)
                };
                var block = InlineText(trimmed[2..], 13, Body);
                Grid.SetColumn(block, 1);
                row.Children.Add(dot);
                row.Children.Add(block);
                _root.Children.Add(row);
                i++;
                continue;
            }

            // 表格
            if (trimmed.StartsWith("|"))
            {
                var rows = new List<string[]>();
                while (i < lines.Length && lines[i].TrimStart().StartsWith("|"))
                {
                    var cells = lines[i].Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                    if (!cells.All(c => c.All(ch => ch == '-' || ch == ':' || ch == ' ')))
                        rows.Add(cells);
                    i++;
                }
                if (rows.Count > 0) _root.Children.Add(Table(rows));
                continue;
            }

            _root.Children.Add(new Border
            {
                Margin = new Thickness(0, 0, 0, 12),
                Child = InlineText(trimmed, 13, Body)
            });
            i++;
        }
    }

    private static Control Heading(string text, double size, IBrush brush, bool withBar)
    {
        var content = InlineText(text, size, brush, FontWeight.SemiBold);
        if (!withBar)
        {
            content.Margin = new Thickness(0, 14, 0, 8);
            return content;
        }

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

    private static Control CodeBlock(string code)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0B0D12")),
            BorderBrush = new SolidColorBrush(Colors.White, 0.065),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(15, 13, 15, 13),
            Margin = new Thickness(0, 4, 0, 10),
            Child = new TextBlock
            {
                Text = code,
                FontFamily = Mono,
                FontSize = 11.5,
                LineHeight = 20,
                Foreground = new SolidColorBrush(Color.Parse("#B9C1D4")),
                TextWrapping = TextWrapping.NoWrap
            }
        };
    }

    private static Control Table(IReadOnlyList<string[]> rows)
    {
        var grid = new Grid
        {
            Margin = new Thickness(0, 4, 0, 16)
        };
        var cols = rows.Max(r => r.Length);
        for (var c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var isHead = r == 0;
            for (var c = 0; c < cols; c++)
            {
                var cell = c < rows[r].Length ? rows[r][c] : string.Empty;
                Control content = isHead
                    ? new TextBlock
                    {
                        Text = cell.ToUpperInvariant(),
                        FontSize = 11,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#9BA3B7"))
                    }
                    : InlineText(cell, 12, new SolidColorBrush(Color.Parse("#B0B8CB")));

                var border = new Border
                {
                    Padding = new Thickness(10, isHead ? 7 : 8, 10, isHead ? 7 : 8),
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
    private static TextBlock InlineText(string text, double size, IBrush brush,
        FontWeight weight = FontWeight.Normal)
    {
        var inlines = new InlineCollection();
        var tb = new TextBlock
        {
            FontSize = size,
            Foreground = brush,
            FontWeight = weight,
            LineHeight = size * 1.75,
            TextWrapping = TextWrapping.Wrap
        };

        var buffer = new System.Text.StringBuilder();

        void Flush()
        {
            if (buffer.Length == 0) return;
            inlines.Add(new Run { Text = buffer.ToString() });
            buffer.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
            {
                var end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > 0)
                {
                    Flush();
                    inlines.Add(new Run
                    {
                        Text = text[(i + 2)..end],
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#E8EAEF"))
                    });
                    i = end + 1;
                    continue;
                }
            }

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
                        FontSize = size - 1.5,
                        Foreground = CodeBrush
                    });
                    i = end;
                    continue;
                }
            }

            buffer.Append(text[i]);
        }
        Flush();
        tb.Inlines = inlines;
        return tb;
    }
}
