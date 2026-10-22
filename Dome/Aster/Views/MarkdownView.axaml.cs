using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Aster.ViewModels;

// ============================================================================
// Markdown 文档模块的视图后台代码 —— 只做两件「用纯绑定做不到」的事：
//   1) 行号同步：TextBox 内部的 ScrollViewer 无法在 XAML 里命名，只能在 Loaded 后从视觉树捞出，
//      订阅 ScrollChanged，把滚动偏移量反向施加到行号栏 Gutter 的 TranslateTransform 上；
//   2) 模式布局：切换 编辑/分栏/预览 时要同时改根 Grid 的列宽与预览栏宽度，绑定表达不了这种联动。
// 三栏结构：文件树（列 0，固定 212）· 编辑区（列 1，星号）· 预览区（列 2，Auto / 420）
// 数据来源：DataContext 即 MarkdownViewModel；本类不产生任何业务状态，只读 VM.Mode
// 启用状态：该模块当前已从一级导航移除、未在大厅启用，代码保留待恢复
// 易踩的坑：ApplyMode 里预览模式的 Width 必须设 double.NaN（Auto），否则 420 的旧值会把列宽锁死；
//           列宽只在 Loaded 之后才能改，构造函数阶段 ColumnDefinitions 还没生成
// ============================================================================
namespace Aster.Views;

/// <summary>
/// Markdown 视图。负责行号栏跟随滚动、以及三种显示模式下的列宽/可见性切换。
/// 其余交互（筛选、选文档、切标签、切模式）全部由 MarkdownViewModel 的命令驱动。
/// </summary>
public partial class MarkdownView : UserControl
{
    // 编辑器 TextBox，用于向下找模板内部的 ScrollViewer
    // 生命周期：构造函数里是 null，OnLoaded 时由 FindControl("Editor") 赋值，此后不再变化。
    private TextBox? _editor;
    // 编辑器内部的滚动条，是行号同步的唯一信号源
    // 生命周期：OnLoaded 里从 _editor 的视觉树捞出；捞不到（模板结构变了）就一直为 null，
    //           此时 SyncGutter 会静默返回，界面表现为「行号不跟着滚」而不是崩溃。
    private ScrollViewer? _scroll;
    // 右侧预览栏，模式切换时改它的 Width 与 IsVisible
    private Border? _preview;
    // 根三列 Grid，模式切换时改第 2、3 列的宽度
    // 注意注释里的「第 2、3 列」是按人眼数（1 起），代码里用的是下标 1 与 2（0 起）。
    private Grid? _root;
    // 行号栏文本块，纵向平移由它承载
    private TextBlock? _gutter;
    // 行号栏的平移变换，XAML 里没有声明，由本类在 Loaded 时创建并注入到 RenderTransform
    // 每次加载只创建一个实例并反复改 Y，不重建变换对象（重建会带来一次额外的属性通知与重绘）。
    private TranslateTransform? _gutterOffset;
    // ViewModel 引用，仅用于读取 Mode，不写回
    // 用途只有两个：OnVmChanged 里读 Mode 判断；另有一处 ApplyMode 里读 _vm.Mode。
    // 注意这里持有 VM 的强引用，并且订阅了它的 PropertyChanged —— 没有退订逻辑，
    // 因为视图与 VM 同生共死；若将来允许换 DataContext，必须先解绑，否则旧 VM 会一直被监听。
    private MarkdownViewModel? _vm;

    /// <summary>
    /// 构造：只加载 XAML 并挂上 Loaded 回调。
    /// 控件引用必须等到 Loaded 再取，此时模板已展开、ColumnDefinitions 已生成。
    /// </summary>
    public MarkdownView()
    {
        // 步骤 1：解析并构建 XAML 里静态声明的控件树。
        // InitializeComponent 由 XAML 编译器生成：按名称作用域登记所有 x:Name，
        // 并把可视树按嵌套关系搭好（本类后面用 FindControl 取名，而不是用生成的字段）。
        // 它只覆盖「XAML 里写死」的部分：TextBox 内部的 ScrollViewer 属于控件模板，此刻还不存在；
        // DataContext 也还没被父级赋上（DataTemplate 的流程是 new 视图 → 再设 DataContext）。
        // 步骤 2：把真正的初始化推迟到 Loaded。
        // 不能在这里直接做的两个原因：
        // ① 行号同步要拿 TextBox 模板里的 ScrollViewer，而模板要 ApplyTemplate 之后才展开；
        // ② 模式布局要读 DataContext，构造期它还是 null。
        // 顺带更正类头注释的一句：x:Name 的控件（含 RootGrid 与它的 ColumnDefinitions）
        // 在 InitializeComponent 返回时就已经生成，构造函数里 FindControl 也能拿到；
        // 真正必须等到 Loaded 的是上面那两条。
        // 副作用提醒：Loaded 在控件每次重新挂到视觉树上时都可能再触发，本类没有防重入，
        // 第二次执行会给 _gutter 换一个新的 TranslateTransform、给 _scroll 再挂一个处理器。
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// 加载完成后的初始化：取控件引用 → 注入行号平移变换 → 订阅编辑器滚动 → 订阅 VM 模式变化 → 应用一次布局。
    /// 副作用：_gutter 从此拥有一个 RenderTransform；_scroll 上多一个 ScrollChanged 处理器；_vm 上多一个 PropertyChanged 处理器。
    /// </summary>
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // 触发时机：在 Avalonia 的启动链路里，一个控件大致经历「构造 → 挂到视觉树（AttachedToVisualTree）
        // → 应用控件模板 → Loaded → Measure → Arrange → Render」。
        // Loaded 比构造晚、比首次绘制早，此时模板已展开、视觉树完整，但尺寸未必已经算过一次 ——
        // 所以这里只取控件引用与订阅事件，没有任何「读 ActualWidth」这类依赖布局结果的操作。
        // （真正按列宽做布局的是 ApplyMode，但它改的是列定义的「期望值」，由下一次 Measure 去消费，
        //   并不需要当前尺寸已知。）
        // 前置状态：各字段都是 null；DataContext 通常已由父级设好（但不保证）。
        // 后置状态：_editor / _preview / _root / _gutter 非空（前提是名字没被改）；
        //           _gutter 的 RenderTransform 换成本类创建的 TranslateTransform；
        //           _scroll 上多一个 ScrollChanged 处理器；_vm 上多一个 PropertyChanged 处理器；
        //           最后按当前 Mode 应用一次布局。全程不写 VM 的任何状态。
        // 步骤 1：取四处命名控件。FindControl 走本控件的 XAML 名称作用域，
        //         只能找到 MarkdownView.axaml 里 x:Name 过的元素；找不到返回 null，不抛异常，
        //         所以下面每个使用者都做了非空判断（宁可少一个功能，也不要空引用崩溃）。
        _editor = this.FindControl<TextBox>("Editor");
        _preview = this.FindControl<Border>("PreviewPane");
        _root = this.FindControl<Grid>("RootGrid");
        _gutter = this.FindControl<TextBlock>("Gutter");
        // 行号栏的位移变换在这里现建现挂：XAML 中只声明了 ClipToBounds，没有 RenderTransform，
        // 好处是布局阶段的 TranslateTransform 不影响测量，只影响绘制位置
        // 步骤 2：注入平移变换。三处取舍：
        // ① 用 RenderTransform 而不是 LayoutTransform：前者只改绘制位置、不进 Measure/Arrange，
        //    每帧改 Y 只触发一次重绘；后者会让布局跟着动，滚动时等于每帧全量重排。
        // ② 不在 XAML 里声明、而在代码里 new：XAML 里声明只能给一个静态值，
        //    而这里需要每帧改同一个变换实例的 Y（Transform 是可变对象，改属性即生效）。
        // ③ 不用绑定 ScrollViewer.Offset：Offset 是普通 CLR 属性、没有对应的 AvaloniaProperty，
        //    不能当绑定源；ScrollViewer 只提供 VerticalOffset 的读值和 ScrollChanged 事件。
        // 为什么 Y 取负：编辑器内容向上滚 offset，行号栏并不在那个 ScrollViewer 里，
        // 想让它「看起来跟着滚」，就得把整块行号往反方向（上）平移同样的距离。
        // 只处理 Y：正文 NoWrap 且横向滚动条 Hidden，X 恒为 0。
        if (_gutter is not null)
        {
            _gutterOffset = new TranslateTransform();
            _gutter.RenderTransform = _gutterOffset;
        }

        if (_editor is not null)
        {
            // 步骤 3：从 TextBox 的视觉树下探它模板里的 ScrollViewer。
            // 模板内部元素没有 x:Name 可供 FindControl，只能按类型在视觉树里找：
            // GetVisualDescendants 是深度优先遍历，OfType 过滤出所有 ScrollViewer，
            // FirstOrDefault 取视觉树顺序的第一个 —— 也就是 TextBox 模板里那层外层滚动容器。
            // 这是「按结构假设」取控件：将来换主题、模板里滚动容器的嵌套顺序变了，
            // 这里可能取到别的实例，表现是行号不再同步，而不是编译期报错。
            // TextBox 的 ScrollViewer 藏在控件模板里，没有名字可绑，只能沿视觉树下探取第一个
            _scroll = _editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            // 滚动的每一帧都会触发，回调里只做一次浮点赋值，开销可以忽略，无需节流
            // 用 lambda 订阅的代价：拿不到委托实例，也就无法 ScrollChanged -= 退订；
            // 又因为没有防重入，OnLoaded 二次执行会叠加第二个处理器（两个都做同样的赋值，结果正确但白跑）。
            // ScrollChanged 里覆盖了「用户拖动滚动条」「滚轮」「键盘翻页」「代码里改 Offset」等所有路径，
            // 所以不需要额外监听 Text 变化去重算偏移。
            if (_scroll is not null)
                _scroll.ScrollChanged += (_, _) => SyncGutter();
        }

        // 步骤 4：订阅 VM。用模式匹配而不是强转，是为了容忍 DataContext 为 null 或类型不对
        // （模块未挂载、设计器预览、宿主塞了别的 VM，都会落到这两种情况）。
        // 现实状态：本工程里没有任何地方 new MarkdownViewModel，所以运行时这条判断恒为假 ——
        // 不订阅、也不调用 ApplyMode，界面停在 XAML 的初始列宽（212 / * / Auto）。
        // 已知脆弱点：只在 Loaded 这一瞬判断一次 DataContext；若父级在 Loaded 之后才赋值
        // （例如 VM 异步就绪），这里整段跳过，而且没有 DataContextChanged 兜底 ——
        // 既不跟随 Mode 变化，也不会应用一次初始布局。
        if (DataContext is MarkdownViewModel vm)
        {
            _vm = vm;
            vm.PropertyChanged += OnVmChanged;
            // 首次进入也要对齐一次：XAML 里的列宽只是初始值，可能和 VM 的当前 Mode 不一致
            // 具体到本视图：XAML 三列写的是「编辑形态」（212 / * / Auto），
            // 而 VM 的 _mode 初值是「分栏」，两者天然不一致；
            // 不补这一次就会出现「分段控件高亮在分栏、实际布局却是编辑」的首帧错位。
            ApplyMode();
        }
    }

    /// <summary>
    /// 监听 VM 属性变化。只关心 Mode —— 其它属性（Text、Filter 等）都由 XAML 绑定自行消化，
    /// 这里多处理反而会重复布局。
    /// </summary>
    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 调用链：VM 的任何属性变化都会进这里（本类只为它订阅了这一次）。
        // e.PropertyName 是属性名字符串；按 INotifyPropertyChanged 的约定，null 表示「所有属性都变了」。
        // 命中 Mode 时调 ApplyMode 改列宽，其余情况直接返回，不做任何事。
        // 为什么只关心 Mode：Text / SaveState / Filter 等变化都有 XAML 绑定接管，
        // 这里再处理一遍只会重复布局；而「改 Grid 的 ColumnDefinition.Width」绑不出来，只能走代码。
        // 一处小隐患：这里用 == 与字符串比较，null（全量刷新）不会被当成 Mode；
        // 当前 VM 用 [CallerMemberName] 发通知，不会出现 null，所以没暴露。
        // 执行顺序不确定：同一次 Mode 变更里，XAML 的 IsVisible 绑定与这个处理器都会被通知，
        // 谁先谁后取决于订阅顺序；因为两边写的值一致、ApplyMode 也可重复执行，所以看不出差异。
        if (e.PropertyName == nameof(MarkdownViewModel.Mode)) ApplyMode();
    }

    /// <summary>
    /// 行号栏跟随编辑器纵向滚动。
    /// 做法是给行号栏一个与滚动方向相反的平移量：编辑器向下滚 offset，行号栏就向上移同样距离，
    /// 视觉上两者保持同一条基线。横向滚动被禁用，所以只需处理 Y。
    /// </summary>
    private void SyncGutter()
    {
        // 调用方只有一个：OnLoaded 里给 _scroll 挂的那个 ScrollChanged lambda（无参重载，忽略事件数据）。
        // 读 ScrollViewer.Offset（Point，含 X/Y），这里只取 Y。
        // 两个空判断分别对应：XAML 里 Gutter 的名字被改（_gutter 为 null）、
        // 或 TextBox 模板里没捞出 ScrollViewer；两种情况都静默返回，表现为「行号不跟着滚」。
        // 不调 InvalidateVisual：Transform 是可变对象，改 Y 自身就会触发重绘，
        // 但不会触发重新测量 —— 行号文本的排版尺寸始终按「全部行」算，滚出视口的部分靠 ClipToBounds 裁掉。
        // 对齐的前提：行号栏与文本框的 FontSize(12.2) / LineHeight(21) / 顶部 Padding(10) 完全一致，
        // 只要有一项不同，同样的 offset 也会逐行累积偏差，越往下越明显。
        if (_gutterOffset is null || _scroll is null) return;
        // 取负是这套方案的核心：Offset.Y 表示「内容相对视口向上移了多少」，
        // 行号栏不在那个 ScrollViewer 里，所以要主动往上移同样的量，视觉上才与正文同一基线。
        _gutterOffset.Y = -_scroll.Offset.Y;
    }

    /// <summary>
    /// 按当前 Mode 应用三栏布局。输入取自 _vm.Mode，输出是对 ColumnDefinitions 与预览栏的直接改写。
    /// 副作用：改列宽、改预览栏 Width、改预览栏 IsVisible —— 这三者必须一起改，
    /// 否则会出现「列宽已归零但控件仍可见」造成的空白条。
    /// </summary>
    private void ApplyMode()
    {
        // 调用方两处：OnLoaded 的末尾（首次对齐）与 OnVmChanged 命中 Mode 时（每次切模式）。
        // 前置：_vm / _preview / _root 都已赋值；Mode 是三个中文值之一。
        // 后置：RootGrid 的列 1、列 2 宽度被改写，预览栏的 Width 与 IsVisible 被改写；
        //       布局在下一个 Measure 周期生效（列宽变化会让 Grid 立即失效并重新测量）。
        // 本方法不做任何判断「值是否已相同」的优化：重复调用只是重复写同样的值，
        // Avalonia 的属性系统会自己判等，代价可以忽略。
        // 三件事必须一起改的原因：「列宽」「可见性」「预览栏 Width」各自影响布局的不同阶段，
        // 只改其中一两项就会留下空白条或宽度错位（例如列宽归零但 IsVisible 还是 true，
        // 控件仍参与布局，只是被裁成 0 宽；或者 Width=420 压住星号列，预览永远不放大）。
        // 这与 XAML 里的 IsVisible 绑定形成了「双写」：两边结论一致，所以看不出冲突；
        // 但如果将来只改一边（比如删掉绑定），就会互相打脸 —— 后写的值胜出。
        // 防御：任一依赖为空（模块未挂载、字段没取到）直接放弃，绝不半途改一半布局。
        if (_vm is null || _preview is null || _root is null) return;
        // 防御：Columns 尚未生成时（理论上只在 Loaded 前）直接放弃，避免越界
        // 实际上 XAML 里的三段 ColumnDefinitions 在 InitializeComponent 后就存在，
        // 所以这条判断在当前代码里永远不会命中；它的作用只是防止将来有人删列。
        if (_root.ColumnDefinitions.Count < 3) return;

        // 下标是「靠 XAML 里的列顺序」定的契约：0 文件树 / 1 编辑区 / 2 预览区。
        // 若将来在 XAML 里插一列，这里会静默改到错误的列上（编译照过），所以列顺序不要动。
        var editorCol = _root.ColumnDefinitions[1];
        var previewCol = _root.ColumnDefinitions[2];

        // 注意 Mode 是中文枚举值，与 MarkdownViewModel.SetMode 的入参、Modes 里的 Label 三处必须一致
        switch (_vm.Mode)
        {
            case "编辑":
                // 编辑模式：编辑区独占剩余宽度，预览列压到 0 并隐藏
                // 列宽 1* 与「唯一非 0 星号列」等价于「吃满剩余空间」（列 0 是 212 固定）；
                // 预览列用 Absolute 0 而不是 Auto：Auto 会按子元素宽度要 420，压不住。
                // 这里同时设 IsVisible=false，与 XAML 的 ShowPreview 绑定指向同一个结果：
                // 列宽 0 已经让它排不到空间，置 false 只是省掉一次无意义的 Measure。
                // 顺带一提：本分支不改 _preview.Width，420 会一直留着 —— 切到预览分支时会被 NaN 清掉。
                editorCol.Width = new GridLength(1, GridUnitType.Star);
                previewCol.Width = new GridLength(0);
                _preview.IsVisible = false;
                break;

            case "预览":
                // 预览模式：编辑列压到 0，预览列改成星号吃满整窗
                // 顺序有讲究：先给预览列星号，再清 Border.Width。
                // 若只改列宽不清 Width，Auto→Star 虽然会让列变宽，但 Border 仍按 420 居中（水平 Stretch 失效）。
                // 多出来的那块槽位就变成空白；反过来先清 Width 再改列宽也成立，只要两者都在同一个同步块里。
                editorCol.Width = new GridLength(0);
                previewCol.Width = new GridLength(1, GridUnitType.Star);
                _preview.IsVisible = true;
                // NaN 等价于 Width=Auto，必须显式清掉，否则 XAML 里写死的 420 会压住星号列宽
                // 具体机制：Border 的显式 Width 是布局的「期望尺寸」，列宽只是给它的槽位；
                // 槽位比期望值宽时，Border 默认 HorizontalAlignment=Stretch 本可以拉满，
                // 但一旦显式设了 Width，Stretch 就失效了。设成 NaN 才恢复「按槽位自适应」。
                // 注意：编辑列置 0 时没有把编辑器 IsVisible 设 false ——
                // 那件事由 XAML 的 ShowEditor 绑定负责，两边分工不同（列宽在代码、可见性在绑定）。
                _preview.Width = double.NaN;
                break;

            default:
                // 分栏（兜底分支）：编辑 : 预览 = 3 : 2 的观感，但预览保持 420 固定宽、列宽用 Auto，
                // 于是窗口变宽时多出来的空间全部给编辑区，预览宽度稳定不抖动
                // 更正一下上面「3 : 2」的措辞：预览列是 Auto 而不是 2*，所以星号根本不会参与 3:2 分配 ——
                // 实际分法是「Auto 拿走 420 后，剩下的全归编辑列的 3*」。
                // 之所以还写成 3* 而不是 1*：只有一个星号列时份数没有布局意义（都会吃满剩余），
                // 保留 3 应该是想与 VM 里 EditorWidth 的「3 份星号」在字面上对齐 —— 那套属性当前无人使用。
                // 另外这里把 IsVisible 重新置 true、Width 还原成 420：
                // 从「预览」切回来时必须显式还原 Width，否则 NaN 会让预览变成自适应、宽度随内容跳动。
                // default 同时兜住 Mode 是脏值（比如空串）的情况 —— 脏值会被当成分栏处理。
                editorCol.Width = new GridLength(3, GridUnitType.Star);
                previewCol.Width = GridLength.Auto;
                _preview.IsVisible = true;
                _preview.Width = 420;
                break;
        }
    }
}
