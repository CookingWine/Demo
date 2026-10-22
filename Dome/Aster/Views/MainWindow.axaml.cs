// ============================================================
// MainWindow.axaml.cs —— 主窗口 code-behind
// 职责：只处理无边框窗口的「缩放」：把 XAML 里 8 个透明热区的鼠标按下转成系统缩放会话。
// 协作：热区在 MainWindow.axaml 中声明并带 Tag；标题栏拖动/最大化在 ShellView.axaml.cs，
//       两者分工是「窗口边缘归窗口，标题栏归视图」。
// 易踩坑：BeginResizeDrag 必须发生在 PointerPressed 且左键按下时，
//         并且要传原始事件参数，否则系统拿不到鼠标捕获，缩放会中途丢失。
// ============================================================
using System;
using Avalonia.Controls;
using Avalonia.Input;

namespace Aster.Views;

/// <summary>
/// 主窗口。除 InitializeComponent 外只有缩放逻辑，业务状态全部在 MainWindowViewModel。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 构造窗口并加载 XAML；InitializeComponent 之后 x:Name 生成的字段（如 Shell）才可用。
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    // ---------- 拖拽缩放 ----------
    /// <summary>拖拽四边/四角缩放窗口（无边框窗口需要自行把鼠标事件转成系统缩放）。</summary>
    private void OnResizePressed(object? sender, PointerPressedEventArgs e)
    {
        // 最大化状态由系统接管尺寸，此时再进入缩放会话会造成窗口状态错乱
        if (WindowState == WindowState.Maximized) return;
        // 只响应左键，避免右键菜单或中键滚动时误触发
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        // 用 Tag 当作方向标识：一个处理器服务 8 个热区，XAML 里只改 Tag 即可
        if (sender is not Control { Tag: string tag }) return;
        // Tag 字符串与 WindowEdge 枚举同名，直接解析；解析失败说明 XAML 的 Tag 写错了
        if (!Enum.TryParse<WindowEdge>(tag, out var edge)) return;

        // 交给窗口管理器执行真正的缩放：它会捕获鼠标，直到松开才结束
        BeginResizeDrag(edge, e);
    }
}
