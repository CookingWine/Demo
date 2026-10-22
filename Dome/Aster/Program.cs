// ============================================================
// Program.cs —— 进程入口，启动链的第一环
// 职责：配置 AppBuilder 并以「经典桌面」生命周期启动 Avalonia，不承载任何业务逻辑。
// 链路：Main → BuildAvaloniaApp().StartWithClassicDesktopLifetime()
//       → App.Initialize（加载 App.axaml）→ App.OnFrameworkInitializationCompleted（建窗口）。
// 协作：App.axaml 提供全局资源与样式，创窗逻辑在 App.axaml.cs，窗口本体在 Views/MainWindow。
// 易踩坑：BuildAvaloniaApp 必须保持 public static、无参、返回 AppBuilder，
//         XAML 预览器与设计器会反射调用它，改签名会导致预览失败。
// ============================================================
using System;
using Avalonia;

namespace Aster;

/// <summary>
/// 进程入口类。internal 即可：只有 Avalonia 启动流程会触达，
/// 无需向其它程序集暴露。
/// </summary>
internal static class Program
{
    /// <summary>
    /// 桌面 UI 主线程入口。
    /// STAThread 不能去掉：剪贴板、系统文件对话框与部分 COM 组件都要求
    /// 线程处于单线程单元（STA）模型，否则运行期会抛异常。
    /// StartWithClassicDesktopLifetime 选用经典桌面生命周期：由主窗口决定进程存亡，
    /// 主窗口关闭即结束消息循环（与「单视图 / 生命周期由框架托管」的用法区分）。
    /// </summary>
    // ---------- 完整启动时序（全部发生在同一个 UI 线程上，按下列顺序同步执行） ----------
    // 步骤 1：CLR 进入 Main。[STAThread] 已把当前线程声明为单线程单元，
    //         这是后面剪贴板、系统文件对话框、部分 COM 组件能工作的前提。
    // 步骤 2：BuildAvaloniaApp() 只是「装配」：登记 Application 子类、平台后端、字体、日志，
    //         此时没有窗口、没有渲染目标，方法立刻返回一个待执行的 AppBuilder。
    // 步骤 3：StartWithClassicDesktopLifetime(args) 才真正启动。它内部依次做四件事：
    //         3.1 创建 ClassicDesktopStyleApplicationLifetime（ShutdownMode 默认 OnLastWindowClose，
    //             即最后一个窗口关闭就结束进程），并把 args 存进 lifetime.Args；
    //         3.2 AppBuilder.SetupWithLifetime(lifetime) 把 lifetime 挂到 Application 上，
    //             然后 new App() 并调用 App.Initialize()（读 App.axaml，灌入资源与样式）；
    //         3.3 回调 App.OnFrameworkInitializationCompleted()——只有在 lifetime 已经挂好、
    //             平台后端已经就绪之后才会走到这里，所以那个方法里才能拿到
    //             IClassicDesktopStyleApplicationLifetime 并 new MainWindow；
    //         3.4 lifetime.Start(args)：把 desktop.MainWindow 显示出来，进入
    //             Dispatcher.UIThread 的消息循环并阻塞在这里，直到主窗口关闭。
    // 步骤 4：消息循环跑起来之后，DispatcherTimer 的回调才有机会执行。注意它的 2 秒是从
    //         VM 构造那一刻（步骤 3.3 内）就开始计的，不是从循环开始才计；到期后回调把
    //         Stage 从 Boot 推到 Login（细节见 MainWindowViewModel）。
    // 一句话：Main 返回即进程退出；界面上发生的一切都在步骤 3.4 建立的那条消息循环里。
    // ---------- 为什么窗口在 3.3 而不是 3.1 建 ----------
    // Window 构造时要向平台申请顶层句柄（Windows 上是 HWND）并选择呈现方式；
    // 在 3.1 阶段平台对象还没创建，new Window 会直接抛异常，
    // 所以「先有 lifetime、再有窗口」这个顺序不是风格问题，而是硬约束。
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        // 这一步是「启动」而不是「配置」：方法内部同步跑完 Setup（建 App、加载 XAML、
        // 建主窗口）后阻塞在消息循环里，返回时说明主窗口已关闭。
        // 返回的 int 是进程退出码（lifetime.Start 的返回值），这里按表达式体直接丢弃。
        .StartWithClassicDesktopLifetime(args);

    // ---------- AppBuilder 装配 ----------

    /// <summary>
    /// 装配 AppBuilder：确定 Application 子类与平台后端。
    /// Configure&lt;App&gt; 让 App.axaml 中的全局资源与样式生效；
    /// UsePlatformDetect 运行时自动挑选 Win32 / X11 / macOS 后端，无需条件编译；
    /// WithInterFont 注册内嵌 Inter 字体，保证不同机器上中英混排字形稳定；
    /// LogToTrace 把 Avalonia 内部日志写入 Trace，排查绑定错误时可挂监听器查看。
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            // 1) 上一行的 Configure<App>() 只登记「Application 子类的工厂」，此刻并不会 new App；
            //    它同时决定了 App.axaml 里 x:Class="Aster.App" 该绑到哪个类型，
            //    以及预览器（反射调用本方法）该用哪个 App 去取全局资源。
            // 2) UsePlatformDetect：按运行时探测平台，注册窗口后端（Win32 / X11 / macOS）、
            //    渲染后端（Skia）与字体管理器；没有它，任何 Window 都无法创建。
            .UsePlatformDetect()
            // 3) WithInterFont：把随包发布的 Inter 字体注册进字体管理器并设为默认首选；
            //    App.axaml 没有设置 FontFamily，界面上西文的字形实际由这一步决定。
            .WithInterFont()
            // 4) LogToTrace：把 Avalonia 内部日志（绑定错误、布局告警等）写入
            //    System.Diagnostics.Trace，默认级别 Warning；排查绑定失败时挂个监听器即可看到。
            // 四个调用都是「就地修改并返回 this」的链式配置，因此返回值仍是同一个 AppBuilder；
            // 顺序不可乱：Configure 必须最先，后面几步都是往它上面注册组件。
            .LogToTrace();
}
