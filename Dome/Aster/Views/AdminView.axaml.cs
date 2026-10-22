// =============================================================================
// AdminView.axaml.cs：AdminView 的 code-behind，只做 View 层无法用绑定表达的三件事
// 1) 宽度：窗口尺寸变化时把三块内容列收敛到 min(1080, 宽 - 96) 并居中；
// 2) 滚动：工作台 / 流程页的对话集合一有新消息就滚到底；
// 3) 输入：Enter 发送、Shift+Enter 换行（Avalonia 的 TextBox 默认把 Enter 当换行）。
// 三态切换本身完全由 ViewModel 的 IsWorkbench / IsFlow / IsSection 驱动，这里不参与。
// 易踩：FindControl 用的是 XAML 里的字符串 x:Name，改名不会编译报错，只会静默拿不到控件。
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
    /// <summary>内容列的最大宽度，超出后居中留白，不再继续拉宽。</summary>
    private const double ContentMaxWidth = 1080;

    // 这里是单边留白，实际左右一共吃掉 96，与设计稿的「宽 - 96」对应。
    /// <summary>内容列与窗口左右边缘的留白。</summary>
    private const double ContentSideGutter = 48;

    // 缓存的 ViewModel 引用。订阅集合变更事件时必须成对地解绑旧实例，
    // 所以除了判断非空，还要靠它记住「上一次订阅的是谁」。
    private AdminViewModel? _vm;

    /// <summary>构造视图。只做 XAML 装载，数据绑定会在 DataContext 赋值后自动建立。</summary>
    public AdminView()
    {
        InitializeComponent();
    }

    /// <summary>窗口尺寸变化时重算内容列宽度，让工作台、列表页与输入框始终对齐同一列。</summary>
    private void OnContentSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // 取「固定上限 1080」与「窗口宽减去两侧留白」中较小的那个：
        // 宽屏时停在 1080 保证阅读行宽，窄屏时跟着窗口一起缩小、不出现横向滚动。
        var width = Math.Min(ContentMaxWidth, e.NewSize.Width - ContentSideGutter * 2);
        // 窗口被拖到极窄时宽度可能算成 0 或负数，此时放弃设置，让控件保持上一次的宽度。
        if (width <= 0) return;

        // 三块内容列分别设置。它们不在同一视觉树分支里（工作台两块 + 列表页一块），
        // 所以只能逐个写，没法靠一个公共父容器统一约束。
        SetColumnWidth(WorkbenchContent, width);
        SetColumnWidth(WorkbenchComposer, width);
        SetColumnWidth(SectionContent, width);
    }

    /// <summary>
    /// 给单个控件写入固定宽度，带两道保护：控件可能尚未生成（x:Name 写错或模板未应用），
    /// 以及宽度未变时不重复赋值，避免无谓的布局循环。
    /// </summary>
    private static void SetColumnWidth(Control? control, double width)
    {
        if (control is null) return;
        // Width 默认是 NaN（表示"由内容/父容器决定"），所以要单独判断；
        // 0.5 的容差用于吞掉浮点误差与连续的像素级抖动。
        if (double.IsNaN(control.Width) || Math.Abs(control.Width - width) > 0.5)
            control.Width = width;
    }

    /// <summary>
    /// DataContext 被替换时重挂集合监听。先解绑旧 VM 再绑新 VM，
    /// 否则旧实例的集合继续持有本视图的委托，会造成重复滚动甚至内存泄漏。
    /// </summary>
    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        // 第一步：如果之前绑过，先摘掉旧的两路对话集合监听。
        if (_vm is not null)
        {
            _vm.Messages.CollectionChanged -= OnMessagesChanged;
            _vm.FlowMessages.CollectionChanged -= OnMessagesChanged;
        }

        // 第二步：记录新 VM 并重新订阅。工作台与流程页各有一个对话集合，两个都要听。
        _vm = DataContext as AdminViewModel;
        if (_vm is not null)
        {
            _vm.Messages.CollectionChanged += OnMessagesChanged;
            _vm.FlowMessages.CollectionChanged += OnMessagesChanged;
        }

        // 第三步：延后到布局完成再滚到底。DataContext 刚赋值时 ItemsControl 还没生成容器，
        // 此刻调用 ScrollToEnd 拿不到正确的内容高度，会滚不到位；
        // DispatcherPriority.Loaded 保证在首次布局之后执行。
        Dispatcher.UIThread.Post(() =>
        {
            ScrollToEnd("ChatScroll");
            ScrollToEnd("FlowScroll");
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// 任意一路对话集合发生变化（新增、清空等）都滚到底部。
    /// 两个 ScrollViewer 一起滚是无害的：不可见的那一个滚动不会产生任何视觉影响。
    /// 这里同样用 Post + Loaded 优先级，因为集合刚变更时新条目的容器尚未测量，
    /// 必须等这一轮布局结束，ScrollToEnd 才能滚到最后一条。
    /// </summary>
    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.UIThread.Post(() =>
        {
            ScrollToEnd("ChatScroll");
            ScrollToEnd("FlowScroll");
        }, DispatcherPriority.Loaded);

    /// <summary>按 XAML 里的 x:Name 查找 ScrollViewer 并滚动到底；找不到时静默跳过。</summary>
    private void ScrollToEnd(string name)
    {
        var scroll = this.FindControl<ScrollViewer>(name);
        scroll?.ScrollToEnd();
    }

    /// <summary>
    /// 输入框按键约定：Enter 发送，Shift+Enter 换行。
    /// 因为 TextBox 设了 AcceptsReturn=True，Enter 默认会插入换行，这里必须拦下来并置 Handled。
    /// </summary>
    private void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        // 只处理 Enter，其它按键一律放行。
        if (e.Key != Key.Enter) return;
        // 带 Shift 的 Enter 交还给 TextBox，让它照常插入换行。
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        // DataContext 还没就绪时不做任何事，避免空引用。
        if (_vm is null) return;

        // 走与发送按钮相同的命令，保证两条入口行为一致（清空草稿、延迟插入回复）。
        _vm.SendCommand.Execute(null);
        // 标记已处理，阻止 TextBox 在文本里再插一个换行符。
        e.Handled = true;
    }
}
