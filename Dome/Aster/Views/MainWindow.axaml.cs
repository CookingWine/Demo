// ============================================================
// MainWindow.axaml.cs —— 主窗口 code-behind
// 职责：① 处理无边框窗口的「缩放」：把 XAML 里 8 个透明热区的鼠标按下转成系统缩放会话；
//       ② 窗口尺寸的本地记忆：启动时读缓存、关闭时写缓存（数据在 Services.LocalCache）。
// 协作：热区在 MainWindow.axaml 中声明并带 Tag；标题栏拖动/最大化在 ShellView.axaml.cs，
//       两者分工是「窗口边缘归窗口，标题栏归视图」。
// 易踩坑：BeginResizeDrag 必须发生在 PointerPressed 且左键按下时，
//         并且要传原始事件参数，否则系统拿不到鼠标捕获，缩放会中途丢失。
// ============================================================
using System;
using Aster.Services;
using Avalonia.Controls;
using Avalonia.Input;

namespace Aster.Views;

/// <summary>
/// 主窗口。除 InitializeComponent、缩放逻辑与尺寸记忆外没有别的职责，业务状态全部在 MainWindowViewModel。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 构造窗口并加载 XAML；InitializeComponent 之后 x:Name 生成的字段（如 Shell）才可用。
    /// </summary>
    // 谁调用：App.OnFrameworkInitializationCompleted 里的 new MainWindow（对象初始化器形式）。
    // 此时的状态：平台后端已就绪（能创建顶层句柄），但窗口尚未 Show、尚未布局；
    //             DataContext 还是 null，所以 XAML 里的 {Binding} 全部处于未解析状态。
    // InitializeComponent 做的事：调 AvaloniaXamlLoader.Load(this) 解析 MainWindow.axaml，
    //   1) 建出 Border / Panel / 四个视图 / 八个热区的整棵视觉树；
    //   2) 把 Window.Styles 里的 .grip 规则挂到本窗口的样式作用域；
    //   3) 填充 x:Name 生成的字段（Shell 就是这里赋值，之前是 null）；
    //   4) 解析 StaticResource（Bg1、Line2、WindowShadow 等，来自 App.axaml 灌入的令牌）。
    // 做完之后：窗口对象完整但空数据；真正的绑定求值发生在构造返回后由外部赋 DataContext 时。
    // 本地缓存的两处接线也在构造里完成：先把上次的尺寸套上（要在首次布局之前，
    // 否则会先按 1320×850 画一帧再跳到大尺寸，肉眼可见），再挂上 Closing 保存。
    public MainWindow()
    {
        InitializeComponent();
        // 尺寸必须在布局之前覆盖 XAML 里的 Width/Height，改动 XAML 默认值不会影响这里。
        RestoreWindowSize();
        // Closing 而不是 Closed：Closed 时窗口对象已经进入销毁流程，
        // 读 Width/Height 有时能拿到 0（取决于平台实现），Closing 阶段几何信息一定还在。
        Closing += (_, _) => SaveWindowSize();
    }

    // ---------- 窗口尺寸记忆 ----------
    /// <summary>把缓存里的尺寸套到窗口上；没有记录或记录明显非法时保持 XAML 的设计稿尺寸。</summary>
    // 调用点：构造函数（InitializeComponent 之后）。
    // 判定用「小于最小值就当没有」而不是「等于 0 才当没有」：
    // 缓存里可能留着旧版本（那时最小尺寸更小）写下的 1080×700，
    // 直接套上去会被 MinWidth/MinHeight 顶回去，等于白设，不如干脆走默认值。
    // NaN 的情况也靠这条比较自然挡掉：任何与 NaN 的比较都是 false。
    private void RestoreWindowSize()
    {
        var cached = LocalCache.Data.Window;
        if (cached.Width < MinWidth || cached.Height < MinHeight) return;
        Width = cached.Width;
        Height = cached.Height;
    }

    /// <summary>关闭时把当前尺寸写进本地缓存（只在正常状态下记，最大化/最小化不覆盖）。</summary>
    // 为什么不记最大化状态：恢复最大化需要额外区分「上次是不是最大化」，
    // 而窗口是居中打开的，恢复成最大化反而会让用户失去「拖到哪就在哪」的直觉；
    // 只记尺寸 + 居中，行为在任何屏幕配置下都可预期。
    // 宽度为 NaN 的情况在刚创建、尚未布局时可能出现，因此先判 NaN 再写。
    private void SaveWindowSize()
    {
        if (WindowState != WindowState.Normal) return;
        if (double.IsNaN(Width) || double.IsNaN(Height)) return;
        var cached = LocalCache.Data.Window;
        cached.Width = Width;
        cached.Height = Height;
        // 同步写：进程随后就会退出，用延迟写（SaveSoon）大概率来不及执行。
        LocalCache.Save();
    }

    /// <summary>
    /// 首次显示后把尺寸收进当前屏幕可用区域，避免「在 4K 屏存下的 3000 宽，换到 1080p 笔记本后开屏即超框」。
    /// </summary>
    // 放在 OnOpened 而不是构造函数里：WorkingArea 与缩放比例要等窗口挂到某个屏幕上才可靠，
    // 构造函数阶段 Screens.Primary 还不一定有值（多屏热插拔时更明显）。
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var screen = Screens.Primary;
        if (screen is null) return;
        // Scaling 是「逻辑像素 → 物理像素」的倍数，WorkingArea 是物理像素，
        // 而 Width/Height 是逻辑像素（DIP），必须先除掉缩放才能比较。
        var scaling = screen.Scaling <= 0 ? 1 : screen.Scaling;
        var areaWidth = screen.WorkingArea.Width / scaling;
        var areaHeight = screen.WorkingArea.Height / scaling;

        // 收到区域大小为止，但仍不小于设计稿下限（否则三栏布局会被挤坏）。
        if (Width > areaWidth) Width = Math.Max(MinWidth, areaWidth);
        if (Height > areaHeight) Height = Math.Max(MinHeight, areaHeight);
    }

    // ---------- 拖拽缩放 ----------
    /// <summary>拖拽四边/四角缩放窗口（无边框窗口需要自行把鼠标事件转成系统缩放）。</summary>
    // 调用链：XAML 里 8 个热区 Border 各挂 PointerPressed="OnResizePressed"
    //         → 鼠标命中热区 → 冒泡路由到本方法 → 转成系统的缩放会话。
    // 前提状态：窗口不在最大化、左键刚按下、sender 的 Tag 是合法的 WindowEdge 名。
    // 做完之后：窗口进入「缩放会话」，之后鼠标移动不再由 Avalonia 派发 PointerMoved，
    //           而是操作系统直接改窗口尺寸并重绘；直到松开左键才回到普通状态。
    // 说明：本方法不设 e.Handled。事件会继续向上冒泡，但热区之上没有别的处理器，
    //       所以不设也没有副作用（若将来在 Window 上也挂处理器，需要重新考虑谁接管）。
    private void OnResizePressed(object? sender, PointerPressedEventArgs e)
    {
        // 步骤 1：最大化状态下直接放弃。
        // 最大化时窗口几何由系统托管，此时进入缩放会话会立刻把窗口拉回还原尺寸，
        // 表现为「一碰边缘就跳」，而且 WindowState 与真实尺寸会短暂不一致。
        // 系统标题栏的做法是「最大化时拖边缘先还原再缩放」，这里选择更简单的不响应。
        // 最大化状态由系统接管尺寸，此时再进入缩放会话会造成窗口状态错乱
        if (WindowState == WindowState.Maximized) return;
        // 步骤 2：只认左键。PointerPressed 对右键、中键、触控笔同样会触发，
        // 不判断的话右键点边缘也会开始缩放（右键通常要留给上下文菜单）。
        // 用 IsLeftButtonPressed 而不是 PointerUpdateKind：后者的语义随按下/抬起而变，
        // 在 PointerPressed 阶段用「是否按住左键」更直白。
        // 只响应左键，避免右键菜单或中键滚动时误触发
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        // 步骤 3：从 sender 取方向标识。用属性模式一次性完成「非空 + 类型 + 取值」：
        // Tag 在 XAML 里是 object，写 Tag="North" 会被解析成 string；
        // 若将来改成绑定或写成别的对象，这里的模式匹配会失败并安全返回。
        // 用 Tag 当作方向标识：一个处理器服务 8 个热区，XAML 里只改 Tag 即可
        if (sender is not Control { Tag: string tag }) return;
        // 步骤 4：把 Tag 解析成系统枚举。
        // Enum.TryParse 默认区分大小写，所以 Tag 必须写成 NorthWest 而不是 northwest；
        // 另外它也接受数字字符串（"3" 同样能解析成功），因此「解析成功」并不等于
        // 「Tag 写对了方向」——真要防错，应该比对 Enum.IsDefined 或用白名单。
        // Tag 字符串与 WindowEdge 枚举同名，直接解析；解析失败说明 XAML 的 Tag 写错了
        if (!Enum.TryParse<WindowEdge>(tag, out var edge)) return;

        // 步骤 5：交给窗口管理器。平台层（Windows 上是发 WM_NCLBUTTONDOWN 加对应的
        // HT 命中码）随即进入模态缩放循环：由系统捕获鼠标、跟随移动、松开左键结束。
        // 正因为捕获在系统手里，这里既不需要 e.Pointer.Capture(this)，也不需要写
        // PointerMoved / PointerReleased 收尾——会话期间的这些事件根本不会派发给控件。
        // 必须传原始的 PointerPressedEventArgs：平台要从里面取时间戳与按键状态，
        // 自己 new 一个参数会丢掉这些信息。调用后本方法立刻返回，缩放仍在继续，
        // 属于「同步发起、异步结束」的调用。
        // 交给窗口管理器执行真正的缩放：它会捕获鼠标，直到松开才结束
        BeginResizeDrag(edge, e);
    }
}
