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

    /// <summary>窗口尺寸变化时重算内容列宽度，让工作台、列表页与输入框始终对齐同一列。</summary>
    private void OnContentSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var width = Math.Min(ContentMaxWidth, e.NewSize.Width - ContentSideGutter * 2);
        if (width <= 0) return;

        SetColumnWidth(WorkbenchContent, width);
        SetColumnWidth(WorkbenchComposer, width);
        SetColumnWidth(SectionContent, width);
    }

    private static void SetColumnWidth(Control? control, double width)
    {
        if (control is null) return;
        if (double.IsNaN(control.Width) || Math.Abs(control.Width - width) > 0.5)
            control.Width = width;
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.Messages.CollectionChanged -= OnMessagesChanged;
            _vm.FlowMessages.CollectionChanged -= OnMessagesChanged;
        }

        _vm = DataContext as AdminViewModel;
        if (_vm is not null)
        {
            _vm.Messages.CollectionChanged += OnMessagesChanged;
            _vm.FlowMessages.CollectionChanged += OnMessagesChanged;
        }

        Dispatcher.UIThread.Post(() =>
        {
            ScrollToEnd("ChatScroll");
            ScrollToEnd("FlowScroll");
        }, DispatcherPriority.Loaded);
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.UIThread.Post(() =>
        {
            ScrollToEnd("ChatScroll");
            ScrollToEnd("FlowScroll");
        }, DispatcherPriority.Loaded);

    private void ScrollToEnd(string name)
    {
        var scroll = this.FindControl<ScrollViewer>(name);
        scroll?.ScrollToEnd();
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
