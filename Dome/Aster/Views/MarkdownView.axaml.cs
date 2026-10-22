using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Aster.ViewModels;

// ============================================================================
// Markdown 文档模块的视图后台代码 —— 只做两件「用纯绑定做不到」的事：
//   1) 行号同步：TextBox 内部的 ScrollViewer 无法在 XAML 里命名，只能在 Loaded 后从视觉树捞出，
//      订阅 ScrollChanged，把滚动偏移量反向施加到行号栏 Gutter 的 TranslateTransform 上；
//   2) 模式布局：切换 编辑/分栏/预览 时要同时改根 Grid 的列宽与预览栏宽度，绑定表达不了这种联动。
// 三栏结构：文件树（列 0，固定 212）· 编辑区（列 1，星号）· 预览区（列 2，Auto / 420）
// 数据来源：DataContext 即 MarkdownViewModel；本类不产生任何业务状态，只读 VM.Mode
// 启用状态：该模块当前已从一级导航移除、未在大厅启用，代码保留待恢复
// 易踩的坑：ApplyMode 里预览模式的 Width 必须设 double.NaN（Auto），否则 420 的旧值会把列宽锁死；
//           列宽只在 Loaded 之后才能改，构造函数阶段 ColumnDefinitions 还没生成
// ============================================================================
namespace Aster.Views;

/// <summary>
/// Markdown 视图。负责行号栏跟随滚动、以及三种显示模式下的列宽/可见性切换。
/// 其余交互（筛选、选文档、切标签、切模式）全部由 MarkdownViewModel 的命令驱动。
/// </summary>
public partial class MarkdownView : UserControl
{
    // 编辑器 TextBox，用于向下找模板内部的 ScrollViewer
    private TextBox? _editor;
    // 编辑器内部的滚动条，是行号同步的唯一信号源
    private ScrollViewer? _scroll;
    // 右侧预览栏，模式切换时改它的 Width 与 IsVisible
    private Border? _preview;
    // 根三列 Grid，模式切换时改第 2、3 列的宽度
    private Grid? _root;
    // 行号栏文本块，纵向平移由它承载
    private TextBlock? _gutter;
    // 行号栏的平移变换，XAML 里没有声明，由本类在 Loaded 时创建并注入到 RenderTransform
    private TranslateTransform? _gutterOffset;
    // ViewModel 引用，仅用于读取 Mode，不写回
    private MarkdownViewModel? _vm;

    /// <summary>
    /// 构造：只加载 XAML 并挂上 Loaded 回调。
    /// 控件引用必须等到 Loaded 再取，此时模板已展开、ColumnDefinitions 已生成。
    /// </summary>
    public MarkdownView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// 加载完成后的初始化：取控件引用 → 注入行号平移变换 → 订阅编辑器滚动 → 订阅 VM 模式变化 → 应用一次布局。
    /// 副作用：_gutter 从此拥有一个 RenderTransform；_scroll 上多一个 ScrollChanged 处理器；_vm 上多一个 PropertyChanged 处理器。
    /// </summary>
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _editor = this.FindControl<TextBox>("Editor");
        _preview = this.FindControl<Border>("PreviewPane");
        _root = this.FindControl<Grid>("RootGrid");
        _gutter = this.FindControl<TextBlock>("Gutter");
        // 行号栏的位移变换在这里现建现挂：XAML 中只声明了 ClipToBounds，没有 RenderTransform，
        // 好处是布局阶段的 TranslateTransform 不影响测量，只影响绘制位置
        if (_gutter is not null)
        {
            _gutterOffset = new TranslateTransform();
            _gutter.RenderTransform = _gutterOffset;
        }

        if (_editor is not null)
        {
            // TextBox 的 ScrollViewer 藏在控件模板里，没有名字可绑，只能沿视觉树下探取第一个
            _scroll = _editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            // 滚动的每一帧都会触发，回调里只做一次浮点赋值，开销可以忽略，无需节流
            if (_scroll is not null)
                _scroll.ScrollChanged += (_, _) => SyncGutter();
        }

        if (DataContext is MarkdownViewModel vm)
        {
            _vm = vm;
            vm.PropertyChanged += OnVmChanged;
            // 首次进入也要对齐一次：XAML 里的列宽只是初始值，可能和 VM 的当前 Mode 不一致
            ApplyMode();
        }
    }

    /// <summary>
    /// 监听 VM 属性变化。只关心 Mode —— 其它属性（Text、Filter 等）都由 XAML 绑定自行消化，
    /// 这里多处理反而会重复布局。
    /// </summary>
    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MarkdownViewModel.Mode)) ApplyMode();
    }

    /// <summary>
    /// 行号栏跟随编辑器纵向滚动。
    /// 做法是给行号栏一个与滚动方向相反的平移量：编辑器向下滚 offset，行号栏就向上移同样距离，
    /// 视觉上两者保持同一条基线。横向滚动被禁用，所以只需处理 Y。
    /// </summary>
    private void SyncGutter()
    {
        if (_gutterOffset is null || _scroll is null) return;
        _gutterOffset.Y = -_scroll.Offset.Y;
    }

    /// <summary>
    /// 按当前 Mode 应用三栏布局。输入取自 _vm.Mode，输出是对 ColumnDefinitions 与预览栏的直接改写。
    /// 副作用：改列宽、改预览栏 Width、改预览栏 IsVisible —— 这三者必须一起改，
    /// 否则会出现「列宽已归零但控件仍可见」造成的空白条。
    /// </summary>
    private void ApplyMode()
    {
        if (_vm is null || _preview is null || _root is null) return;
        // 防御：Columns 尚未生成时（理论上只在 Loaded 前）直接放弃，避免越界
        if (_root.ColumnDefinitions.Count < 3) return;

        var editorCol = _root.ColumnDefinitions[1];
        var previewCol = _root.ColumnDefinitions[2];

        // 注意 Mode 是中文枚举值，与 MarkdownViewModel.SetMode 的入参、Modes 里的 Label 三处必须一致
        switch (_vm.Mode)
        {
            case "编辑":
                // 编辑模式：编辑区独占剩余宽度，预览列压到 0 并隐藏
                editorCol.Width = new GridLength(1, GridUnitType.Star);
                previewCol.Width = new GridLength(0);
                _preview.IsVisible = false;
                break;

            case "预览":
                // 预览模式：编辑列压到 0，预览列改成星号吃满整窗
                editorCol.Width = new GridLength(0);
                previewCol.Width = new GridLength(1, GridUnitType.Star);
                _preview.IsVisible = true;
                // NaN 等价于 Width=Auto，必须显式清掉，否则 XAML 里写死的 420 会压住星号列宽
                _preview.Width = double.NaN;
                break;

            default:
                // 分栏（兜底分支）：编辑 : 预览 = 3 : 2 的观感，但预览保持 420 固定宽、列宽用 Auto，
                // 于是窗口变宽时多出来的空间全部给编辑区，预览宽度稳定不抖动
                editorCol.Width = new GridLength(3, GridUnitType.Star);
                previewCol.Width = GridLength.Auto;
                _preview.IsVisible = true;
                _preview.Width = 420;
                break;
        }
    }
}
