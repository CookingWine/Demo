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
// 和 HomeView 一样，这里没有任何业务逻辑：三栏布局、内联样式、行模板全在 XAML 里，
// 状态与行为全在 MusicViewModel 与 Controls 目录下的自绘控件里。
// 具体说，本页里"看起来像视图逻辑"的三样东西都不在这个文件：
//   · 唱片旋转   → SpinningDisc 内部的 50ms 定时器，只读 IsSpinning 属性；
//   · 频谱跳动   → AudioBars 内部动画，只读 IsPlaying；
//   · 行内跳动条 → EqIndicator 内部的 120ms 定时器，只读 IsVisible。
// 依赖关系：ShellView 的 ContentControl 拿到 MusicViewModel → 按 DataType 命中模板 → new MusicView()
// → 框架把该实例赋给新控件的 DataContext → XAML 里的 {Binding ...} 才开始取值。
// 也就是说构造函数执行时 DataContext 还是 null，此刻读不到任何曲目信息。
public partial class MusicView : UserControl
{
    // 只做 XAML 加载：内联样式、控制模板、数据绑定都在 InitializeComponent 里建立。
    // InitializeComponent 由 XAML 编译器生成在另一半 partial 类里，做三件事：
    //   1) 调 AvaloniaXamlLoader.Load(this)，解析 MusicView.axaml 并构造可视树；
    //   2) 把带 x:Name 的节点写成字段（本页没有 x:Name，所以不生成字段）；
    //   3) 接上 XAML 里声明的事件处理器（本页没有 Click= / PointerPressed=，所以也没有订阅）。
    // 另外 UserControl.Styles 里的四条样式（Button.song / tab / cbtn 及其伪类）也在这步装进控件自己的样式集合，
    // 所以它们只作用于 MusicView 这棵子树，不影响其它页面 —— 这也是它们没写进全局 Styles/Controls.axaml 的原因。
    public MusicView()
    {
        InitializeComponent();
    }
    // 视图被切走时不做任何清理：控件里的动画计时器由各自的 OnDetachedFromVisualTree 停表，
    // 而 VM 里那个 DispatcherTimer 一直活着（见 MusicViewModel 构造函数的说明）。
}
