// =============================================================================
// AdminView.axaml.cs：AdminView 的 code-behind，只做 View 层无法用绑定表达的三件事
// 1) 宽度：窗口尺寸变化时把各页面内容列收敛到 min(1080, 宽 - 96) 并居中；
// 2) 滚动：工作台对话集合一有新消息就滚到底；
// 3) 输入：Enter 发送、Shift+Enter 换行（Avalonia 的 TextBox 默认把 Enter 当换行）。
// =============================================================================

using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Aster.ViewModels;

namespace Aster.Views;

public partial class AdminView : UserControl
{
    // 1080 来自设计稿的内容列上限：再宽的话表格每行文字会拉得过散，阅读动线变长。
    /// <summary>内容列的最大宽度，超出后居中留白，不再继续拉宽。</summary>
    private const double ContentMaxWidth = 1080;

    // 48 是单侧留白，公式里要乘 2；窗口很窄时这一对留白也保留，宽度不够就由下面的 <= 0 判断兜住。
    /// <summary>内容列与窗口左右边缘的留白。</summary>
    private const double ContentSideGutter = 48;

    // 只保存「当前挂着的 VM」，用途单一：DataContext 换成另一个 VM 时，靠它退订旧对象的事件。
    // 事件是强引用，不退订的话旧 VM 会一直把这个视图（以及它的整棵视觉树）留在内存里。
    private AdminViewModel? _vm;

    public AdminView()
    {
        // 解析并构建 XAML 里的视觉树。执行完之后 x:Name 对应的字段才可用，
        // 这也是为什么滚动、赋宽之类的操作都放在事件回调里，而不是构造函数里。
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        // 调用时机：ShellView 的 ContentControl 换上 AdminViewModel、或视图被复用时。
        // 先后顺序不能换：必须先把旧 VM 的订阅摘掉，再挂新的，否则换 VM 的瞬间
        // 旧 VM 仍然持有这个视图（事件是强引用），会继续往这里推滚动请求。
        // 步骤 1：先交给基类，DataContext 到这一步已经是新值。
        base.OnDataContextChanged(e);
        // 步骤 2：退订旧的、记住新的、订阅新的。顺序固定，保证同一时刻这个视图只挂在一个 VM 上。
        if (_vm is not null) _vm.Messages.CollectionChanged -= OnMessagesChanged;

        _vm = DataContext as AdminViewModel;
        if (_vm is not null) _vm.Messages.CollectionChanged += OnMessagesChanged;

        // 步骤 3：延后滚到底。DataContext 刚变时视觉树往往还没 Measure/Arrange，
        // ScrollViewer 的 Extent 与 Viewport 都还是 0，此刻 ScrollToEnd 等于原地不动。
        // DispatcherPriority.Loaded 把回调排到本轮布局之后，此时内容高度已经算好。
        // _vm 为 null 时也会 Post 一次，只是找不到消息区，属于无害的空跑。
        Dispatcher.UIThread.Post(() => ScrollToEnd("ChatScroll"), DispatcherPriority.Loaded);

        // 注意：这里只在「换 DataContext」时退订，没有重写 OnDetachedFromVisualTree。
        // 若宿主反复重建 AdminView（而不是复用同一个实例），旧视图会一直被 VM 的事件引用，
        // 既释放不掉，收到消息时还会对一棵已经脱离视觉树的树执行 ScrollToEnd。
        // 当前 ShellView 的 ContentControl 会复用模板实例，所以这个问题没有暴露。
    }

    // 每条新消息都 Post 一次滚动，没有防抖或合并：连发两条会排队两次 ScrollToEnd。
    // 结果等价（都是滚到底），代价只是几次多余调用。
    // 监听的是工作台的 Messages，与客户详情 / 审批列表无关。
    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.UIThread.Post(() => ScrollToEnd("ChatScroll"), DispatcherPriority.Loaded);

    // 按名字字符串查找。相对于直接使用 x:Name 生成的字段，这种写法把名字退化成运行期字符串：
    // 写错不会编译报错，只会静默不滚动；FindControl 找不到时返回 null，由 ?. 跳过，
    // 页面还没挂上视觉树时走的就是这个空分支。
    private void ScrollToEnd(string name) => this.FindControl<ScrollViewer>(name)?.ScrollToEnd();

    // 调用方：XAML 里六处 SizeChanged（工作台的 Grid 加五个 ScrollViewer），
    // 窗口缩放、页面首次布局、可见性切换导致重新布局时都会触发。
    // e.NewSize.Width 是「触发事件的那个控件」的宽度，不是窗口宽度：
    // 五个 ScrollViewer 都是整行铺满，工作台 Grid 同理，所以六处可以共用同一套公式。
    /// <summary>窗口尺寸变化时重算各页面内容列宽度，保证六种页面宽度口径一致。</summary>
    private void OnContentSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // 公式：min(1080, 可用宽 - 96)。96 = 左右各 48 的留白（ContentSideGutter × 2）；
        // 减完留白再与 1080 取小 —— 宽屏时内容列不再继续拉长，而是居中留白。
        var width = Math.Min(ContentMaxWidth, e.NewSize.Width - ContentSideGutter * 2);
        // 窗口窄于 96 时结果是 0 或负数，赋给 Width 会被当成非法值；直接放弃这一轮。
        if (width <= 0) return;

        // 七个控件一起赋宽：内容列与输入卡必须同宽，六页之间也必须同口径，
        // 否则切页时标题和表格的左右边界会跳一下。
        SetColumnWidth(WorkbenchContent, width);
        SetColumnWidth(WorkbenchComposer, width);
        SetColumnWidth(CustomersContent, width);
        SetColumnWidth(DetailContent, width);
        SetColumnWidth(ApprovalContent, width);
        SetColumnWidth(WorkflowContent, width);
        SetColumnWidth(KpiContent, width);
    }

    // 给单个控件赋固定宽度，static 是因为它不读实例状态。
    // 为什么要先判 double.IsNaN：Width 的默认值是 NaN（表示「按内容自适应」），
    // 而 NaN 参与减法仍是 NaN、NaN > 0.5 是 false。若省掉这个判断，首次调用会被跳过，
    // 内容列就一直保持 Auto 宽度 —— 这是实际踩过的坑，不是理论防御。
    // 0.5 是像素容差：布局换算会产生亚像素抖动，差值不到半像素就不重复赋值，
    // 避免「赋值 → 触发重排 → 再次 SizeChanged → 再赋值」的来回抖动。
    private static void SetColumnWidth(Control? control, double width)
    {
        // 页签内容尚未构建或被模板回收时，生成字段可能是 null。
        if (control is null) return;
        // 首次（NaN）或宽度真的变了才写；写 Width 会让控件重新 Measure，因此必须收敛。
        if (double.IsNaN(control.Width) || Math.Abs(control.Width - width) > 0.5)
            control.Width = width;
    }

    // 调用方：Composer 输入框的 KeyDown。判定顺序有讲究，不能合并：
    // 1) 不是 Enter 一律放行（字母、退格、方向键交给 TextBox 自己处理）；
    // 2) Enter + Shift 也放行，让 TextBox 插入换行（AcceptsReturn=True 的默认行为）；
    // 3) 剩下的裸 Enter 才是发送；_vm 为 null 时静默返回。
    // e.Handled = true 必须置：KeyDown 是路由事件，不标记已处理的话，TextBox 的默认处理
    // 还会在发送之后插入一个换行符 —— 草稿已经清空，却凭空多出一个空行。
    // 这里执行 VM 的 SendCommand 而不是自己拼消息，保证按钮点击与键盘回车走同一条逻辑。
    private void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        if (_vm is null) return;

        _vm.SendCommand.Execute(null);
        e.Handled = true;
    }
}
