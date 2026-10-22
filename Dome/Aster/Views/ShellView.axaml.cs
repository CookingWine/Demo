// ============================================================
// ShellView.axaml.cs —— 主壳 code-behind：自绘标题栏的窗口交互
// 职责：标题栏按下时拖动窗口、双击切换最大化；三个窗口按钮的最小化/最大化/关闭。
// 协作：XAML 里 TitleBar 的 PointerPressed 指向这里；窗口缩放（四边四角）在 MainWindow.axaml.cs，
//       因为那部分属于窗口本身而不是主壳视图。
// 易踩坑：标题栏里嵌了三个 Button，事件会冒泡上来，
//         必须先判断 e.Source 是不是 Button，否则点最小化会顺带触发拖动。
// ============================================================
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
namespace Aster.Views;

/// <summary>
/// 登录后的主壳视图。这里只放窗口级交互，导航与内容切换都在 MainWindowViewModel。
/// </summary>
public partial class ShellView : UserControl
{
    /// <summary>
    /// 加载 ShellView.axaml；InitializeComponent 之后 TitleBar 等 x:Name 字段才可用。
    /// </summary>
    // 谁调用：MainWindow.axaml 里 <views:ShellView x:Name="Shell" .../> 这一行，
    //         即 MainWindow 的 InitializeComponent 解析 XAML 时——早于窗口 Show，
    //         也早于 MainWindow 的 DataContext 被赋值（所以此刻绑定还取不到值）。
    // InitializeComponent 做的事：解析 ShellView.axaml，建出标题栏 + 三栏布局，
    //   并把 UserControl.Resources / UserControl.DataTemplates 两套模板登记到本控件上，
    //   之后内部的 ItemsControl 与 ContentControl 才找得到模板。
    // 注意：模板是「登记」不是「实例化」，四个页面的 UserControl 要等内容出现才创建。
    public ShellView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 沿视觉树向上取宿主窗口。
    /// 用 TopLevel.GetTopLevel 而不是 FindAncestorOfType&lt;Window&gt;()：前者在视图尚未挂载
    /// （如设计器预览）时直接返回 null，后者依赖视觉树已就绪；
    /// 因此取不到窗口时，所有交互都静默跳过，不会抛异常。
    /// </summary>
    // 每次交互都重新查一次，而不是在构造时缓存窗口引用：
    // 视图可能在视觉树里被移动（换宿主），缓存会在那种场景下指向旧窗口。
    // GetTopLevel 的行为是「沿视觉树向上找第一个 TopLevel（Window / 窗口式弹层）」，
    // 控件还没挂到树上（设计器预览、刚构造完）时返回 null，as Window 也就得到 null。
    private static Window? Host(Visual v) => TopLevel.GetTopLevel(v) as Window;

    /// <summary>
    /// 标题栏按下：左键单击拖动窗口，左键双击在最大化与还原之间切换。
    /// 无边框窗口没有系统标题栏，这两件事必须自己实现。
    /// </summary>
    // 谁调用：ShellView.axaml 中标题栏 Border 的 PointerPressed="OnTitleBarPressed"。
    // 时机与前提：PointerPressed 是冒泡路由事件，从命中元素一路向上冒泡；
    //             能走到这里说明点击没有被更内层的控件吃掉（例如三个窗口按钮）。
    // 做完之后：双击 → 窗口在 Normal / Maximized 之间切换一次；
    //           单击 → 交给窗口管理器进入拖动会话，直到松开左键。
    // 事件流要点：这里不设 e.Handled，事件仍会继续向上冒泡到 Window，
    //             但 Window 上没挂 PointerPressed，所以没有副作用。
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        // 步骤 1：命中来源是按钮就直接退出。
        // 两个理由：一是 Button 已经把 PointerPressed 标成 Handled，正常情况下本方法
        // 根本不会被按钮的点击触发，这行是第二道防线；二是 e.Source 是「命中测试得到的
        // 最内层元素」，可能是 Button 模板里的 Border#Bg、ContentPresenter 或 Icon，
        // 此时 is Button 不成立——所以这个判断只能兜住「命中 Button 本体」的情形。
        // 若将来把按钮内容换成更复杂的控件，应改成沿祖先链找 Button 才可靠。
        // 命中来源是按钮就直接退出：三个窗口按钮的 Click 已经处理了逻辑，
        // 若继续往下走会在拖动的过程中把窗口一起拖走
        if (e.Source is Button) return;
        // 步骤 2：只处理左键。e.Source 判断挡不住右键/中键，这里再确认一次按键状态；
        // 否则右键点标题栏也会让窗口跟着走，而右键通常要留给系统菜单。
        // 只处理左键：e.Source 判断挡不住右键/中键，这里再确认一次按键状态
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // 步骤 3：取宿主窗口。设计器里为 null，直接放弃（不做任何补偿逻辑）。
        var window = Host(this);
        if (window is null) return;

        // 步骤 4：区分单击与双击。ClickCount 由平台累计（同一位置、系统双击间隔内），
        // 因此这里不需要自己计时；==2 之后直接 return，避免「双击的第二下又启动拖动」。
        // ClickCount==2 表示双击：这里是「切换」而不是「置为最大化」，
        // 因为最大化状态下再双击应当还原，跟系统标题栏的行为保持一致
        if (e.ClickCount == 2)
        {
            // 三元表达式在这里就是「toggle」：Maximized → Normal，其余 → Maximized。
            // 注意 Minimized 状态也能被双击切到 Maximized，但最小化时标题栏不可见，
            // 实际走不到这个分支。
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        // 步骤 5：单击拖动。BeginMoveDrag 把后续交给窗口管理器：它捕获鼠标、
        // 跟随指针移动整个窗口，直到松开左键结束。
        // 因此不需要自己 PointerMoved / PointerReleased 收尾，也不该在这里再设置
        // e.Pointer.Capture——捕获权在系统手里，Avalonia 期间收不到这些事件。
        // 必须传原始事件参数：平台要从里面取指针 ID、时间戳与按键状态。
        // BeginMoveDrag 由窗口管理器接管后续鼠标移动，直到松开左键；
        // 必须传原始事件参数，窗口才能正确做鼠标捕获
        window.BeginMoveDrag(e);
    }

    /// <summary>最小化：直接改 WindowState，交给窗口管理器处理动画与任务栏状态。</summary>
    // 谁调用：标题栏第一枚按钮的 Click（XAML 里 Click="OnMinimize"）。
    // 做完之后：窗口收进任务栏，恢复由系统负责（点任务栏、Alt+Tab 都行），
    //           本类不需要处理「恢复」，因为窗口实例一直活着、状态没变。
    private void OnMinimize(object? sender, RoutedEventArgs e)
    {
        var w = Host(this);
        if (w is not null) w.WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// 最大化/还原切换。与标题栏双击共用同一套判断，
    /// 这样按钮和双击不会出现「一个能还原、一个不能」的不一致。
    /// </summary>
    // 谁调用：标题栏第二枚按钮的 Click（Click="OnMaximize"）。
    // 前提：窗口处于 Normal 或 Maximized；三元表达式把「当前状态」直接映射成「目标状态」，
    //       所以同一个按钮天然就是切换语义。
    // 做完之后：窗口尺寸被系统改到工作区大小（无边框窗口没有标题栏，客户区就是整块）。
    // 已知现象：按钮里的图标写死成 Icons.Square，最大化后不会变成「还原」图标，
    //           与 Windows 原生行为不一致（见文件末尾的问题记录）。
    private void OnMaximize(object? sender, RoutedEventArgs e)
    {
        var w = Host(this);
        if (w is null) return;
        w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    /// <summary>关闭主窗口：经典桌面生命周期下即结束整个应用。</summary>
    // 谁调用：标题栏第三枚按钮（Classes="chrome close"）的 Click。
    // 链路：Window.Close() → 触发 Closed → 经典桌面生命周期的 ShutdownMode 是
    //       OnLastWindowClose，最后一个窗口关闭即调用 Shutdown → Dispatcher 主循环退出
    //       → StartWithClassicDesktopLifetime 返回 → Main 返回 → 进程结束。
    // 注意：没有任何「保存状态 / 二次确认」逻辑，VM 里的数据随进程一起丢弃；
    //       演示项目够用，接真实业务时通常要在这里插一道确认。
    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Host(this)?.Close();
    }

    // ===== 本文件相关的已知观察（只记录，不改代码） =====
    // 1) 最大化按钮的图标固定是 Icons.Square：进入最大化后不会变成「还原」图标，
    //    与系统标题栏的行为不一致，用户看不出当前是否已最大化。
    //    要修需要在 OnMaximize 里换 Icons，或给按钮加一个「当前状态」绑定。
    // 2) 标题栏按下时的 e.Source is Button 只能识别「命中 Button 本体」；
    //    若命中它模板里的 Border / Icon，这条判断不成立。当前之所以不出问题，
    //    是因为 Button 自己会把 PointerPressed 标成 Handled，事件根本到不了这里；
    //    哪天改用 AddHandler(..., handledEventsToo: true) 就会暴露这个脆弱点。
    // 3) 三个处理器都用 Host(this) 现查窗口，而不是缓存字段：多一次向上遍历，
    //    换来的是「视图换宿主也不会指向旧窗口」，对这个层级的调用频率完全值得。
}
