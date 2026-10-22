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
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// 框架初始化完成后的回调：在这里创建主窗口。
    /// 只处理经典桌面生命周期；若将来改成单视图或浏览器宿主，走不到这个分支。
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        // 生命周期类型判断：只有桌面宿主才存在 MainWindow 的概念
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // DataContext 由对象初始化器赋值：窗口已构造完、尚未首次布局，绑定就已就绪；
            // MainWindowViewModel 构造函数里启动 2s 的 DispatcherTimer，把 Boot 推进到 Login
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };
        }

        // 交给基类收尾（完成主题应用、生命周期事件等），必须放在最后
        base.OnFrameworkInitializationCompleted();
    }
}
