// ============================================================
// ShellView.axaml.cs —— 主壳 code-behind：自绘标题栏的窗口交互
// 职责：标题栏按下时拖动窗口、双击切换最大化；三个窗口按钮的最小化/最大化/关闭。
// 协作：XAML 里 TitleBar 的 PointerPressed 指向这里；窗口缩放（四边四角）在 MainWindow.axaml.cs，
//       因为那部分属于窗口本身而不是主壳视图。
// 易踩坑：标题栏里嵌了三个 Button，事件会冒泡上来，
//         必须先判断 e.Source 是不是 Button，否则点最小化会顺带触发拖动。
// ============================================================
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
namespace Aster.Views;

/// <summary>
/// 登录后的主壳视图。这里只放窗口级交互，导航与内容切换都在 MainWindowViewModel。
/// </summary>
public partial class ShellView : UserControl
{
    /// <summary>
    /// 加载 ShellView.axaml；InitializeComponent 之后 TitleBar 等 x:Name 字段才可用。
    /// </summary>
    public ShellView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 沿视觉树向上取宿主窗口。
    /// 用 TopLevel.GetTopLevel 而不是 FindAncestorOfType&lt;Window&gt;()：前者在视图尚未挂载
    /// （如设计器预览）时直接返回 null，后者依赖视觉树已就绪；
    /// 因此取不到窗口时，所有交互都静默跳过，不会抛异常。
    /// </summary>
    private static Window? Host(Visual v) => TopLevel.GetTopLevel(v) as Window;

    /// <summary>
    /// 标题栏按下：左键单击拖动窗口，左键双击在最大化与还原之间切换。
    /// 无边框窗口没有系统标题栏，这两件事必须自己实现。
    /// </summary>
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        // 命中来源是按钮就直接退出：三个窗口按钮的 Click 已经处理了逻辑，
        // 若继续往下走会在拖动的过程中把窗口一起拖走
        if (e.Source is Button) return;
        // 只处理左键：e.Source 判断挡不住右键/中键，这里再确认一次按键状态
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var window = Host(this);
        if (window is null) return;

        // ClickCount==2 表示双击：这里是「切换」而不是「置为最大化」，
        // 因为最大化状态下再双击应当还原，跟系统标题栏的行为保持一致
        if (e.ClickCount == 2)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        // BeginMoveDrag 由窗口管理器接管后续鼠标移动，直到松开左键；
        // 必须传原始事件参数，窗口才能正确做鼠标捕获
        window.BeginMoveDrag(e);
    }

    /// <summary>最小化：直接改 WindowState，交给窗口管理器处理动画与任务栏状态。</summary>
    private void OnMinimize(object? sender, RoutedEventArgs e)
    {
        var w = Host(this);
        if (w is not null) w.WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// 最大化/还原切换。与标题栏双击共用同一套判断，
    /// 这样按钮和双击不会出现「一个能还原、一个不能」的不一致。
    /// </summary>
    private void OnMaximize(object? sender, RoutedEventArgs e)
    {
        var w = Host(this);
        if (w is null) return;
        w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    /// <summary>关闭主窗口：经典桌面生命周期下即结束整个应用。</summary>
    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Host(this)?.Close();
    }
}
