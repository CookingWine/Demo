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
    public BootView()
    {
        InitializeComponent();
    }
}
