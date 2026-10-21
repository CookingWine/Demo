using Avalonia.Controls;
using Avalonia.Input;
using Aster.ViewModels;

namespace Aster.Views;

public partial class SettingsOverlay : UserControl
{
    public SettingsOverlay()
    {
        InitializeComponent();
    }

    private void OnMaskPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.CloseSettingsCommand.Execute(null);
    }
}
