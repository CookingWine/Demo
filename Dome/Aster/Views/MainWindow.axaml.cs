using System;
using Avalonia.Controls;
using Avalonia.Input;

namespace Aster.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>拖拽四边/四角缩放窗口（无边框窗口需要自行把鼠标事件转成系统缩放）。</summary>
    private void OnResizePressed(object? sender, PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (sender is not Control { Tag: string tag }) return;
        if (!Enum.TryParse<WindowEdge>(tag, out var edge)) return;

        BeginResizeDrag(edge, e);
    }
}
