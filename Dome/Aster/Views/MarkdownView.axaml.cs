using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Aster.ViewModels;

namespace Aster.Views;

public partial class MarkdownView : UserControl
{
    private TextBox? _editor;
    private ScrollViewer? _scroll;
    private Border? _preview;
    private Grid? _root;
    private TextBlock? _gutter;
    private TranslateTransform? _gutterOffset;
    private MarkdownViewModel? _vm;

    public MarkdownView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _editor = this.FindControl<TextBox>("Editor");
        _preview = this.FindControl<Border>("PreviewPane");
        _root = this.FindControl<Grid>("RootGrid");
        _gutter = this.FindControl<TextBlock>("Gutter");
        if (_gutter is not null)
        {
            _gutterOffset = new TranslateTransform();
            _gutter.RenderTransform = _gutterOffset;
        }

        if (_editor is not null)
        {
            _scroll = _editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (_scroll is not null)
                _scroll.ScrollChanged += (_, _) => SyncGutter();
        }

        if (DataContext is MarkdownViewModel vm)
        {
            _vm = vm;
            vm.PropertyChanged += OnVmChanged;
            ApplyMode();
        }
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MarkdownViewModel.Mode)) ApplyMode();
    }

    private void SyncGutter()
    {
        if (_gutterOffset is null || _scroll is null) return;
        _gutterOffset.Y = -_scroll.Offset.Y;
    }

    private void ApplyMode()
    {
        if (_vm is null || _preview is null || _root is null) return;
        if (_root.ColumnDefinitions.Count < 3) return;

        var editorCol = _root.ColumnDefinitions[1];
        var previewCol = _root.ColumnDefinitions[2];

        switch (_vm.Mode)
        {
            case "编辑":
                editorCol.Width = new GridLength(1, GridUnitType.Star);
                previewCol.Width = new GridLength(0);
                _preview.IsVisible = false;
                break;

            case "预览":
                editorCol.Width = new GridLength(0);
                previewCol.Width = new GridLength(1, GridUnitType.Star);
                _preview.IsVisible = true;
                _preview.Width = double.NaN;
                break;

            default:
                editorCol.Width = new GridLength(3, GridUnitType.Star);
                previewCol.Width = GridLength.Auto;
                _preview.IsVisible = true;
                _preview.Width = 420;
                break;
        }
    }
}
