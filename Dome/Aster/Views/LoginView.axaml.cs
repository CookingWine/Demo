// ============================================================================
// LoginView.axaml.cs —— 登录页的 code-behind（只处理纯视图交互，不放业务逻辑）
// 职责：1) 空白处按住拖窗、双击最大化；2) 右上角最小化 / 最大化 / 关闭按钮。
// 与 MainWindowViewModel 的分工：登录、密码显隐、弹窗切换全在 VM 的 Command 里，
// 这里只碰 Window 级别的 API（BeginMoveDrag / WindowState / Close），因为 VM 不该依赖窗口。
// 坑：拖窗必须排除 Button/TextBox/Border.field，否则点输入框会变成拖窗口，无法聚焦输入。
// ============================================================================

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Aster.Views;

/// <summary>
/// 登录页视图。只处理「窗口级」交互：拖窗、双击最大化、最小化/最大化/关闭；
/// 登录、密码显隐、两个弹窗的状态全部走 Binding 交给 MainWindowViewModel，这里不碰业务。
/// </summary>
// ── 为什么这些逻辑必须留在 code-behind ──────────────────────────────────────
// BeginMoveDrag / WindowState / Close 都是 Window 上的 API，而 ViewModel 不应该知道
// 「自己被显示在一个窗口里」（否则无法单测、也无法复用到嵌入式场景）。
// 所以分界线是：能表达成「状态」的交给 VM（IsVisible、Command），
// 只有「必须拿到宿主窗口才能做的事」才留在这里。
//
// ── 事件是怎么到这里的（Avalonia 的路由事件）──────────────────────────────────
// PointerPressed / Click 都是冒泡（Bubble）事件：从被命中的最深元素开始，
// 沿视觉树的父链逐级向上传递，任何一级都能处理，且不会自动停止。
// XAML 里写的 PointerPressed="OnDragPressed" 等价于在挂载的那个控件上 AddHandler，
// 所以处理器收到的事件里 e.Source 是「真正被点中的那个元素」，
// 而 sender 是「挂处理器的那个控件」—— 这两个在本文件里分别是「叶子控件」与「那块 Grid」。
// e.Handled 默认 false；本文件从不设置它，因此事件会继续向上冒泡到 UserControl 与 Window
// （那里没有对应处理器，所以没有副作用）。
public partial class LoginView : UserControl
{
    /// <summary>InitializeComponent 由 XAML 编译器生成：加载 LoginView.axaml 的视觉树，并挂接 Click/PointerPressed 事件。</summary>
    // 具体做了三件事：把 XAML 编译产物实例化成控件树；把 x:Name 的控件赋给同名字段
    // （本页没有 x:Name，所以没有生成字段）；把 XAML 里写的事件名绑定到本类的方法
    // （这一步在编译期生成委托，不是运行期反射）。
    // 它必须在任何使用视觉树的代码之前调用，因此构造函数里只有这一行。
    public LoginView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 沿可视树向上取宿主窗口。用 as 而不是强制转换：TopLevel 在嵌入/设计器预览时可能不是 Window，
    /// 此时返回 null，调用方直接放弃操作即可。
    /// </summary>
    // TopLevel.GetTopLevel 的实现方式就是沿 VisualParent 一路向上找，碰到第一个 TopLevel 就返回；
    // 元素还没挂进视觉树（例如刚 new 出来、尚未 AttachedToVisualTree）时返回 null。
    // 本页只在事件处理器里调用它，那时视图必然已挂接，所以 null 分支实际只为「设计器 / 预览」准备。
    // static 是因为它不读实例状态，只依赖传入的可视元素。
    private static Window? Host(Visual v) => TopLevel.GetTopLevel(v) as Window;

    /// <summary>空白处按住即可拖动窗口；双击切换最大化。输入框与按钮保留自己的交互。</summary>
    // 谁调用：Grid 的 PointerPressed（冒泡到根 Grid 的每一次按下都会进来，包括点在按钮上的那一次）。
    // 前提状态：无需任何前置；窗口可能是 null（设计器），下面会判。
    // 做完之后：三种结局 —— 直接返回（点在交互控件上 / 不是左键 / 没有宿主窗口）、
    // 切换最大化（双击）、开始系统拖窗（单击空白）。
    // 它不修改任何 VM 状态，所以本方法对绑定世界是完全无副作用的。
    private void OnDragPressed(object? sender, PointerPressedEventArgs e)
    {
        // 步骤 0：sender 是挂处理器的 Grid（本方法里用不到），
        // 真正需要的是 e.Source —— 被指针命中的最深元素。
        // 注意 source 可能是 Icon、TextBlock、TextPresenter 这类「看得见但不能交互」的元素，
        // 所以下面 IsInteractive 要沿祖先链判断，而不能只比较类型。

        // 步骤 1：先判断落点是不是交互控件。是的话立刻返回，把事件让给它自己
        //         （按钮要处理按下/抬起、输入框要拿焦点并开始选择文本）。
        // 用 e.Source（真正被命中的元素）而不是 sender（本例是整块 Grid）来判断，
        // 一旦命中按钮/输入框就立刻返回，把事件让给控件自己处理（聚焦、点击等）。
        // 放在按键判断之前只是省一次查询：点在按钮上时连「是不是左键」都不必问。
        if (e.Source is Visual source && IsInteractive(source)) return;
        // 步骤 2：只响应左键（右键/中键按下不该开始拖窗）。
        // 只响应左键：右键/中键按下不应该开始拖窗。
        // GetCurrentPoint(this) 的 this 只是「坐标参照元素」，这里只读 Properties 里的按键状态，
        // 所以传哪个元素都不影响结果；传 this 是因为它就是当前视图，语义最清楚。
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // 步骤 3：拿宿主窗口。取不到（设计器预览 / 尚未挂接）就放弃 —— 拖窗没有目标。
        var window = Host(this);
        if (window is null) return;

        // 步骤 4：双击分支。必须在 BeginMoveDrag 之前拦掉 ——
        //         原因有两层：① BeginMoveDrag 会把控制权交给系统的移动循环并捕获指针，
        //         在鼠标松开前不会回到我们的代码，第二次按下的 ClickCount==2 根本传不进来；
        //         ② 即使传得进来，此时窗口已经在拖拽状态，切换最大化会让系统状态错乱。
        //         ClickCount 由平台累计：按下 → 1、快速第二次按下 → 2（三击会到 3，这里只认 2）。
        // 双击要在进入拖拽之前拦掉，否则 BeginMoveDrag 会先接管鼠标，双击永远到不了这里。
        if (e.ClickCount == 2)
        {
            // 最大化与还原之间切换：用当前状态取反，而不是「总是最大化」，
            // 这样双击在已最大化的窗口上等于还原，符合系统标题栏的行为习惯。
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            // 切换完就返回：这一下按下不应该再触发拖拽。
            return;
        }

        // 步骤 5：交给系统控制窗口移动。
        // 交给系统接管移动：鼠标松开前窗口持续跟随，比手动算偏移量更平滑。
        // 本方法在这里返回后，拖动仍由系统继续；不需要自己调 e.Pointer.Capture，
        // BeginMoveDrag 内部会完成捕获，并在松开时自动释放。
        // 也就是说这一行之后到鼠标松开之间，本类不会再收到任何指针事件。
        window.BeginMoveDrag(e);
    }

    /// <summary>命中按钮（含 CheckBox）、输入框或输入框外框时不拖拽，把事件让给控件本身。</summary>
    // 判定分三层，从「命中元素自己」逐层放大到「它的祖先」：
    //   第 1 层  元素类型本身：Button / TextBox（含派生类）
    //   第 2 层  祖先里有没有 Button / TextBox（点了图标、文字、文本呈现层）
    //   第 3 层  祖先（或自身）里最近的 Border 是否带 field 类（点了输入框的内边距）
    // 三层都返回 false 时，才认为是「空白处」，交给调用方开始拖窗。
    //
    // 关于 includeSelf:false：
    //   FindAncestorOfType 从给定元素开始向上找；includeSelf:true 会把 source 自己也参与匹配。
    //   第 2 层写 false 是因为第 1 层已经处理过「source 本身就是 Button/TextBox」的情况，
    //   再包含自身只是重复判断；写 false 让「这一层只负责祖先」的意图更明确。
    //   第 3 层相反，必须先用 `source as Border` 手动补上「自身是 Border」的情形 ——
    //   因为点在内边距上时，命中的正是 Border 本身（有 Background 的元素才参与命中测试），
    //   而 FindAncestorOfType(includeSelf:false) 会跳过它。
    private static bool IsInteractive(Visual source)
    {
        // 第 1 层：元素本身就是交互控件。
        // 直接点在控件本体上：Button / TextBox（CheckBox 也继承自 Button 系列，同样被拦下）。
        // 继承链是 CheckBox → ToggleButton → Button，所以模式匹配一个 Button 就覆盖了复选框与眼睛按钮。
        if (source is Button or TextBox) return true;
        // 第 2 层之一：祖先里有按钮（例如 Button 内部的 Icon / TextBlock）。
        // 点在按钮内部的图标或文字上：命中元素是子级，要找祖先按钮才能判断出来。
        // 例如主按钮上的文字、chrome 按钮里的 ctrl:Icon —— 它们自己不是 Button。
        if (source.FindAncestorOfType<Button>(includeSelf: false) is not null) return true;
        // 第 2 层之二：祖先里有输入框（例如 TextBox 内部的 TextPresenter）。
        // 同理处理输入框内部的元素（如 TextBox 的文本呈现层）。
        // TextBox 的模板里有一层 TextPresenter，点在文字上时 e.Source 是它而不是 TextBox。
        if (source.FindAncestorOfType<TextBox>(includeSelf: false) is not null) return true;

        // 第 3 层：看最近的那个 Border 是不是带 field 类的输入框外框。
        // Border.field 是输入框的外框，点它的内边距也应该聚焦输入框而不是拖窗口
        // 写法是「自己是 Border 就用自己，否则找最近的 Border 祖先」——
        // 对应两种落点：内边距的空白（命中 Border 自身）、图标与文字之间的缝（命中更深层）。
        // 只检查最近的这一个 Border 是够用的：field 外框内部没有别的 Border，
        // 而其它带 Border 的地方（如联系管理员弹窗里的名片）根本不在 Grid 的祖先链上，
        // 不会走到这个处理器。
        var border = source as Border ?? source.FindAncestorOfType<Border>(includeSelf: false);
        // Classes.Contains 判断的是 XAML 里写的 Classes="field"，与样式选择器 Border.field 用的是同一套类名。
        return border is not null && border.Classes.Contains("field");
    }

    /// <summary>最小化：直接改 WindowState，系统会处理动画与还原。</summary>
    // 谁触发：右上角第一枚 chrome 按钮（XAML 里 Click="OnMinimize"）。
    // Click 由 Button 自己在「按下并在按钮内松开」时触发，是按钮级的语义事件；
    // 它出现之前，同一次按下的 PointerPressed 已经冒泡到根 Grid，被 IsInteractive 拦掉了 ——
    // 所以这两个机制不冲突：拖窗不会启动，Click 照常派发。
    // 只改 WindowState、不隐藏窗口：任务栏仍保留该项，点任务栏可以还原。
    private void OnMinimize(object? sender, RoutedEventArgs e)
    {
        // 取不到窗口（设计器预览）就什么都不做，也不抛异常。
        var w = Host(this);
        if (w is not null) w.WindowState = WindowState.Minimized;
    }

    /// <summary>最大化 / 还原切换（与双击空白处的逻辑一致）。</summary>
    // 谁触发：右上角第二枚 chrome 按钮（Click="OnMaximize"）。
    // 与 OnDragPressed 里的双击分支是同一套判断，两处并存而不是互相调用，
    // 因为一个入口是按钮、一个是指针事件，参数类型不同（RoutedEventArgs vs PointerPressedEventArgs）。
    // 注意：最大化后两栏仍按 42 : 58 的星号比例拉伸，布局不会错乱（这是用比例而不是固定像素的收益）；
    // 窗口的圆角与投影是否保留由外层 Border 决定，与 WindowState 无关。
    private void OnMaximize(object? sender, RoutedEventArgs e)
    {
        var w = Host(this);
        if (w is null) return;
        // 三元取反：最大化 → 还原，其余（Normal / Minimized）→ 最大化。
        // 若窗口处于最小化状态被点到这里（实际不可能，因为按钮不可见），会直接变成最大化。
        w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    /// <summary>关闭窗口（无二次确认，因为登录页本身没有未保存的数据）。</summary>
    // 谁触发：右上角第三枚 chrome close 按钮（Click="OnClose"）。
    // 用 ?. 而不是 if：宿主窗口为 null 时什么都不做（与上面两个方法同样的防御）。
    // Close 之后 Avalonia 的默认关闭策略是「最后一个窗口关闭即退出进程」，
    // 所以点这里等价于退出应用 —— 此时 VM 里的 UserName/Password 随进程一起丢弃。
    private void OnClose(object? sender, RoutedEventArgs e) => Host(this)?.Close();
}
