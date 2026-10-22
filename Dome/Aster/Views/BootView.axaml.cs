// ============================================================
// BootView.axaml.cs —— 启动闪屏的 code-behind
// 职责：只有构造与 XAML 加载，没有任何逻辑：闪屏的动画在 XAML 关键帧里，
//       阶段切换（Boot → Login）由 MainWindowViewModel 的 DispatcherTimer 负责。
// 协作：MainWindow 通过 IsBoot 控制其显隐；DataContext 继承自 MainWindow 的 MainWindowViewModel。
// 坑：不要在这里写启动逻辑，闪屏只是「看得见的等待」，时序源头在 VM，写两份必然不同步。
// ============================================================
using Avalonia.Controls;

namespace Aster.Views;

/// <summary>
/// 启动闪屏视图。保持「零代码」是有意为之：视图只负责呈现，时序归 ViewModel。
/// </summary>
public partial class BootView : UserControl
{
    /// <summary>
    /// 加载 BootView.axaml；动画在 XAML 加载时即开始，无需在此启动。
    /// </summary>
    // 谁调用：MainWindow.axaml 里 <views:BootView IsVisible="{Binding IsBoot}" /> ——
    //         MainWindow 的 InitializeComponent 解析 XAML 时就实例化了它，
    //         也就是「应用启动那一刻」，此时窗口还没 Show、DataContext 还没赋值。
    // 为什么这里什么都不做：进度条动画由 XAML 里的样式动画驱动，时钟随「挂上视觉树」自动开始；
    //   阶段切换由 MainWindowViewModel 的 2 秒 DispatcherTimer 负责。
    //   一旦在这里再写一份启动逻辑，就会和 VM 的时序形成两套真相，早晚不同步。
    // 补一句时序：IsVisible 绑定 IsBoot，构造时 DataContext 还是 null，绑定取不到值；
    //   等 App 赋完 DataContext，绑定求值得到 true，这个页面才真正显示出来。
    public BootView()
    {
        InitializeComponent();
    }
}
