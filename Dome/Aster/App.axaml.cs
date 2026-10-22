// ============================================================
// App.axaml.cs —— Application 子类，负责全局初始化与主窗口创建
// 职责：Initialize 加载 App.axaml（资源 + 样式）；OnFrameworkInitializationCompleted
//       在框架初始化完成后创建 MainWindow，并注入 MainWindowViewModel 作为 DataContext。
// 位置：Program.Main → AppBuilder → 本类，是「XAML 资源」到「窗口实例」的接缝。
// 协作：MainWindow 内部 Panel 叠 ShellView / LoginView / BootView / SettingsOverlay，
//       由 MainWindowViewModel 的 AppStage 与 IsSettingsOpen 控制显隐。
// 易踩坑：DataContext 在构造前用对象初始化器赋值，早于窗口首次布局，
//         所以首帧就能拿到正确的 IsBoot，不会闪一下空白。
// ============================================================
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Aster.ViewModels;
using Aster.Views;

namespace Aster;

/// <summary>
/// 应用对象：持有全局资源与样式，并决定启动时展示哪个窗口。
/// partial 是因为 App.axaml 会生成同名的另一半（含 InitializeComponent）。
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 加载 App.axaml，把 Theme.axaml 与 Controls.axaml 灌进应用的资源/样式树。
    /// 此时还没有任何窗口，早于 OnFrameworkInitializationCompleted。
    /// </summary>
    // 谁调用：AppBuilder.SetupWithLifetime 内部的 Setup()，整个进程只调用一次。
    // 调用时的状态：平台后端与渲染后端已注册完毕，但还一个 Window 都没有；
    //               Application.Current 已指向 this，所以 XAML 里的资源查找有根可循。
    // 调用后的状态：Application.Resources 里已有 Theme.axaml 的全部令牌，
    //               Application.Styles 里已有 FluentTheme + Controls.axaml。
    // 这解释了后面所有视图为什么能直接写 {StaticResource Bg1}：
    // StaticResource 是「解析到这一行就必须能查到键」的求值方式，
    // 只有在窗口 XAML 加载之前把令牌灌好，窗口才不会在启动时抛「找不到资源」。
    // 为什么不用编译器生成的 InitializeComponent：两者等价（生成的版本也是调
    // AvaloniaXamlLoader.Load(this)，外加 DEBUG 下的资源重复检查），这里显式写出来
    // 是为了让「加载的是哪个文件」在代码里一眼可见，而不是藏在 obj 目录的生成代码里。
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// 框架初始化完成后的回调：在这里创建主窗口。
    /// 只处理经典桌面生命周期；若将来改成单视图或浏览器宿主，走不到这个分支。
    /// </summary>
    // 谁调用：AppBuilder.SetupWithLifetime，位置在 App.Initialize() 之后、
    //         lifetime.Start(args) 进入消息循环之前，同步回调且只回调一次。
    // 为什么此刻 ApplicationLifetime 才可用：SetupWithLifetime 会先把 lifetime 实例
    //         赋给 Application.ApplicationLifetime，再回调本方法；在 Initialize 里读它
    //         只会拿到 null。模式匹配失败说明宿主不是桌面形态（单视图/浏览器），
    //         那种宿主自己管理视图，不应该在这里建 Window。
    // 为什么窗口要等到这里才建：Window 构造时要向平台申请顶层句柄（HWND）并选呈现方式，
    //         平台对象没就绪就 new 会直接抛异常。
    // 做完之后的状态：desktop.MainWindow 指向一个已绑定好 DataContext 的窗口，
    //         随后 lifetime.Start 负责 Show 它并进入 Dispatcher 消息循环。
    public override void OnFrameworkInitializationCompleted()
    {
        // 生命周期类型判断：只有桌面宿主才存在 MainWindow 的概念
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 下面这四行是一条有顺序的链，拆成三步看：
            // 步骤 1：new MainWindow()。构造函数只做 InitializeComponent：解析
            //         MainWindow.axaml、建出视觉树、填充 x:Name 生成的字段。
            //         此刻 DataContext 仍是 null，XAML 里的 {Binding} 全部处于「未解析」
            //         状态（不报错，但也取不到值），所以这一步只是把壳搭起来。
            // 步骤 2：对象初始化器赋 DataContext。赋值触发 DataContextChanged，
            //         整棵子树的绑定立刻求值：IsBoot 得到 true → BootView 可见，
            //         Shell / Login / SettingsOverlay 得到 false → 隐藏。
            //         先构造、后赋 DataContext 的顺序保证了首帧就是正确画面，
            //         不会先闪一下空窗口再切到闪屏。
            // 步骤 3：把窗口登记给 lifetime。这里只是登记，Show 发生在 lifetime.Start 里；
            //         关闭它即命中 ShutdownMode 默认值 OnLastWindowClose，进程随之退出。
            // 另注：步骤 2 里的 new MainWindowViewModel() 会立刻启动 2 秒的 DispatcherTimer
            //         （计时从这一刻就走，不等消息循环），而窗口要到 lifetime.Start 才 Show；
            //         所以屏幕上真正看到 Boot 的时长是 2000ms 减去「Show + 首帧布局」那几十毫秒，
            //         只会略短、不会更长。要精确控制可见时长，得把计时器挪到窗口 Opened 之后。
            // DataContext 由对象初始化器赋值：窗口已构造完、尚未首次布局，绑定就已就绪；
            // MainWindowViewModel 构造函数里启动 2s 的 DispatcherTimer，把 Boot 推进到 Login
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };
        }

        // 交给基类收尾（完成主题应用、生命周期事件等），必须放在最后
        // 基类的实现依赖「MainWindow 已经就位」这一状态，提前调用会让窗口错过
        // 主题应用与启动事件的时机。
        base.OnFrameworkInitializationCompleted();
    }
}
