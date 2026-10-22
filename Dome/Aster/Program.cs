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
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
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
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
