using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Aster.ViewModels;

namespace Aster.Views;

public partial class AdminView : UserControl
{
    private AdminViewModel? _vm;

    public AdminView()
    {
        InitializeComponent();
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
