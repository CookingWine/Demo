// ============================================================================
// LoginView.axaml.cs —— 登录页的 code-behind（只处理纯视图交互，不放业务逻辑）
// 职责：1) 空白处按住拖窗、双击最大化；2) 右上角最小化 / 最大化 / 关闭按钮。
// 与 MainWindowViewModel 的分工：登录、密码显隐、弹窗切换全在 VM 的 Command 里，
// 这里只碰 Window 级别的 API（BeginMoveDrag / WindowState / Close），因为 VM 不该依赖窗口。
// 坑：拖窗必须排除 Button/TextBox/Border.field，否则点输入框会变成拖窗口，无法聚焦输入。
// ============================================================================

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Aster.Views;

/// <summary>
/// 登录页视图。只处理「窗口级」交互：拖窗、双击最大化、最小化/最大化/关闭；
/// 登录、密码显隐、两个弹窗的状态全部走 Binding 交给 MainWindowViewModel，这里不碰业务。
/// </summary>
public partial class LoginView : UserControl
{
    /// <summary>InitializeComponent 由 XAML 编译器生成：加载 LoginView.axaml 的视觉树，并挂接 Click/PointerPressed 事件。</summary>
    public LoginView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 沿可视树向上取宿主窗口。用 as 而不是强制转换：TopLevel 在嵌入/设计器预览时可能不是 Window，
    /// 此时返回 null，调用方直接放弃操作即可。
    /// </summary>
    private static Window? Host(Visual v) => TopLevel.GetTopLevel(v) as Window;

    /// <summary>空白处按住即可拖动窗口；双击切换最大化。输入框与按钮保留自己的交互。</summary>
    private void OnDragPressed(object? sender, PointerPressedEventArgs e)
    {
        // 用 e.Source（真正被命中的元素）而不是 sender（本例是整块 Grid）来判断，
        // 一旦命中按钮/输入框就立刻返回，把事件让给控件自己处理（聚焦、点击等）。
        if (e.Source is Visual source && IsInteractive(source)) return;
        // 只响应左键：右键/中键按下不应该开始拖窗。
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var window = Host(this);
        if (window is null) return;

        // 双击要在进入拖拽之前拦掉，否则 BeginMoveDrag 会先接管鼠标，双击永远到不了这里。
        if (e.ClickCount == 2)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        // 交给系统接管移动：鼠标松开前窗口持续跟随，比手动算偏移量更平滑。
        window.BeginMoveDrag(e);
    }

    /// <summary>命中按钮（含 CheckBox）、输入框或输入框外框时不拖拽，把事件让给控件本身。</summary>
    private static bool IsInteractive(Visual source)
    {
        // 直接点在控件本体上：Button / TextBox（CheckBox 也继承自 Button 系列，同样被拦下）。
        if (source is Button or TextBox) return true;
        // 点在按钮内部的图标或文字上：命中元素是子级，要找祖先按钮才能判断出来。
        if (source.FindAncestorOfType<Button>(includeSelf: false) is not null) return true;
        // 同理处理输入框内部的元素（如 TextBox 的文本呈现层）。
        if (source.FindAncestorOfType<TextBox>(includeSelf: false) is not null) return true;

        // Border.field 是输入框的外框，点它的内边距也应该聚焦输入框而不是拖窗口
        var border = source as Border ?? source.FindAncestorOfType<Border>(includeSelf: false);
        return border is not null && border.Classes.Contains("field");
    }

    /// <summary>最小化：直接改 WindowState，系统会处理动画与还原。</summary>
    private void OnMinimize(object? sender, RoutedEventArgs e)
    {
        var w = Host(this);
        if (w is not null) w.WindowState = WindowState.Minimized;
    }

    /// <summary>最大化 / 还原切换（与双击空白处的逻辑一致）。</summary>
    private void OnMaximize(object? sender, RoutedEventArgs e)
    {
        var w = Host(this);
        if (w is null) return;
        w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    /// <summary>关闭窗口（无二次确认，因为登录页本身没有未保存的数据）。</summary>
    private void OnClose(object? sender, RoutedEventArgs e) => Host(this)?.Close();
}
