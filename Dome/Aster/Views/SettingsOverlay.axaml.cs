// ============================================================================
// SettingsOverlay.axaml.cs —— 设置浮层的代码隐藏，只保留「点遮罩关闭」这一个交互
// 职责：把 XAML 里遮罩 Border 的 PointerPressed 事件转成 MainWindowViewModel 的关闭命令。
// 为什么放在代码隐藏：点击遮罩关闭是纯视图行为，用事件比在 VM 里暴露一个 MaskPressedCommand 更直接，
// 也不会让 SettingsViewModel 反过来依赖窗口的显隐状态。
// 关系：XAML 根节点 x:DataType 是 MainWindowViewModel（Settings 只是它的一个属性），
// 所以这里取 DataContext 时也按 MainWindowViewModel 判断类型，而不是 SettingsViewModel。
// 坑：本类是 partial，InitializeComponent 由 XAML 编译器生成，删掉这行调用会导致控件树为空。
// ============================================================================

using Avalonia.Controls;
using Avalonia.Input;
using Aster.ViewModels;

namespace Aster.Views;

/// <summary>
/// 设置浮层的视图外壳。面板内容全部由 XAML 里的隐式 DataTemplate 驱动，
/// 这里不持有任何业务状态，只负责把遮罩上的点击翻译成关闭命令。
/// </summary>
public partial class SettingsOverlay : UserControl
{
    public SettingsOverlay()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 遮罩（XAML 里那个 #BD030407 的 Border）被按下时关闭浮层。
    /// 只绑在遮罩上，不绑在 Panel 上：Panel 会收到面板内部冒泡上来的点击，
    /// 那样点设置项也会顺手把浮层关掉；遮罩是面板的下层兄弟，只有点面板外的区域才会命中它。
    /// </summary>
    private void OnMaskPressed(object? sender, PointerPressedEventArgs e)
    {
        // 走命令而不是直接改 IsSettingsOpen：关闭入口与标题栏的关闭按钮、底部的「完成」保持同一条路径，
        // 将来关闭时要附带副作用（比如落盘），只改命令实现即可
        if (DataContext is MainWindowViewModel vm) vm.CloseSettingsCommand.Execute(null);
    }
}
