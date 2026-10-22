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
    /// <summary>内容列的最大宽度，超出后居中留白，不再继续拉宽。</summary>
    private const double ContentMaxWidth = 1080;

    /// <summary>内容列与窗口左右边缘的留白。</summary>
    private const double ContentSideGutter = 48;

    private AdminViewModel? _vm;

    public AdminView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) _vm.Messages.CollectionChanged -= OnMessagesChanged;

        _vm = DataContext as AdminViewModel;
        if (_vm is not null) _vm.Messages.CollectionChanged += OnMessagesChanged;

        Dispatcher.UIThread.Post(() => ScrollToEnd("ChatScroll"), DispatcherPriority.Loaded);
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.UIThread.Post(() => ScrollToEnd("ChatScroll"), DispatcherPriority.Loaded);

    private void ScrollToEnd(string name) => this.FindControl<ScrollViewer>(name)?.ScrollToEnd();

    /// <summary>窗口尺寸变化时重算各页面内容列宽度，保证六种页面宽度口径一致。</summary>
    private void OnContentSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var width = Math.Min(ContentMaxWidth, e.NewSize.Width - ContentSideGutter * 2);
        if (width <= 0) return;

        SetColumnWidth(WorkbenchContent, width);
        SetColumnWidth(WorkbenchComposer, width);
        SetColumnWidth(CustomersContent, width);
        SetColumnWidth(DetailContent, width);
        SetColumnWidth(ApprovalContent, width);
        SetColumnWidth(WorkflowContent, width);
        SetColumnWidth(KpiContent, width);
    }

    private static void SetColumnWidth(Control? control, double width)
    {
        if (control is null) return;
        if (double.IsNaN(control.Width) || Math.Abs(control.Width - width) > 0.5)
            control.Width = width;
    }

    private void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        if (_vm is null) return;

        _vm.SendCommand.Execute(null);
        e.Handled = true;
    }
}
