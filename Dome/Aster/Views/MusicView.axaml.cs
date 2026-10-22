// ============================================================================
// MusicView.axaml.cs —— 音乐播放器视图的代码后置（code-behind）
// 职责：只调用 InitializeComponent() 加载 XAML，所有状态都由 MusicViewModel 提供。
// 约定：播放计时器不在视图里，而在 ViewModel 的 DispatcherTimer 中，保证视图可以随时重建。
// 位置：MusicView 当前未挂到一级导航（导航只保留「管理 / 设置」），保留待恢复。
// 坑：XAML 里没有 x:Name，因此这里没有控件字段，也没有事件处理需要写。
// ============================================================================
using Avalonia.Controls;

namespace Aster.Views;

/// <summary>音乐播放器视图：DataContext 由 ShellView 的 DataTemplate 按 VM 类型注入。</summary>
public partial class MusicView : UserControl
{
    // 只做 XAML 加载：内联样式、控制模板、数据绑定都在 InitializeComponent 里建立。
    public MusicView()
    {
        InitializeComponent();
    }
}
