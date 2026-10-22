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
// 这个类一共只有一件事：把 HomeView.axaml 编译产物挂上来。它没有任何状态、没有任何字段，
// 所以「首页长什么样、显示什么数据」这个问题，答案全部在 .axaml 与 HomeViewModel 两处，不在这里。
// 依赖关系：ShellView 的 ContentControl 拿到 HomeViewModel 实例 → 按 DataType 命中模板 → new HomeView()
// → 框架把该实例赋给新控件的 DataContext → XAML 里的 {Binding ...} 才开始取值。
// 也就是说构造 HomeView 时 DataContext 还是 null，所有绑定要等到加入可视树、DataContext 被赋值后才求值。
public partial class HomeView : UserControl
{
    // 只做 XAML 加载：绑定、样式、模板都在 InitializeComponent 里挂接完成。
    // InitializeComponent 是 XAML 编译器生成的另一半 partial 类方法，它做三件事：
    //   1) 调 AvaloniaXamlLoader.Load(this)，解析 HomeView.axaml 并按节点构造可视树；
    //   2) 把带 x:Name 的节点写成字段（本页没有 x:Name，所以不生成任何字段）；
    //   3) 接上 XAML 里声明的事件处理器（本页没有 Click= / PointerPressed=，所以也没有订阅）。
    // 这一步不涉及绑定求值：绑定要等 DataContext 注入、控件进入可视树后才生效。
    public HomeView()
    {
        InitializeComponent();
    }
    // 如果将来真要在这一页加交互（例如卡片点击、滚动联动），按工程约定优先加 ICommand 到 HomeViewModel；
    // 只有纯视图层的东西（指针捕获、尺寸测量、动画）才写回这里，避免把业务状态塞进 code-behind。
}
