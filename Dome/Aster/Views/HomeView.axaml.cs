// ============================================================================
// HomeView.axaml.cs —— 首页视图的代码后置（code-behind）
// 职责：只调用 InitializeComponent() 加载 XAML；数据全部通过 DataContext 上的 HomeViewModel 绑定。
// 约定：这里刻意不写业务逻辑，保持「视图只负责呈现」的 MVVM 边界。
// 位置：HomeView 当前未挂到一级导航（导航只保留「管理 / 设置」），保留待恢复。
// 坑：XAML 里没有 x:Name，因此这里不会生成控件字段，也没有事件处理需要写。
// ============================================================================
using Avalonia.Controls;

namespace Aster.Views;

/// <summary>首页视图：DataContext 由 ShellView 的 DataTemplate 按 VM 类型注入。</summary>
public partial class HomeView : UserControl
{
    // 只做 XAML 加载：绑定、样式、模板都在 InitializeComponent 里挂接完成。
    public HomeView()
    {
        InitializeComponent();
    }
}
