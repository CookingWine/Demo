using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Aster.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
    }

    private static Window? Host(Visual v) => TopLevel.GetTopLevel(v) as Window;

    private void OnMinimize(object? sender, RoutedEventArgs e)
    {
        var w = Host(this);
        if (w is not null) w.WindowState = WindowState.Minimized;
    }

    private void OnMaximize(object? sender, RoutedEventArgs e)
    {
        var w = Host(this);
        if (w is null) return;
        w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Host(this)?.Close();
}
