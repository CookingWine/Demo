using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Aster.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
    }

    private static Window? Host(Visual v) => TopLevel.GetTopLevel(v) as Window;

    /// <summary>空白处按住即可拖动窗口；双击切换最大化。输入框与按钮保留自己的交互。</summary>
    private void OnDragPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source && IsInteractive(source)) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var window = Host(this);
        if (window is null) return;

        if (e.ClickCount == 2)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        window.BeginMoveDrag(e);
    }

    /// <summary>命中按钮（含 CheckBox）、输入框或输入框外框时不拖拽，把事件让给控件本身。</summary>
    private static bool IsInteractive(Visual source)
    {
        if (source is Button or TextBox) return true;
        if (source.FindAncestorOfType<Button>(includeSelf: false) is not null) return true;
        if (source.FindAncestorOfType<TextBox>(includeSelf: false) is not null) return true;

        // Border.field 是输入框的外框，点它的内边距也应该聚焦输入框而不是拖窗口
        var border = source as Border ?? source.FindAncestorOfType<Border>(includeSelf: false);
        return border is not null && border.Classes.Contains("field");
    }

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
