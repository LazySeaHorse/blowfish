using Avalonia.Controls;
using Avalonia.Interactivity;
using ImageCaptionSearch.UI.ViewModels;

namespace ImageCaptionSearch.UI.Views;

public partial class ImageDetailView : UserControl
{
    public ImageDetailView()
    {
        InitializeComponent();
    }

    private async void OnCopyCaptionClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ImageDetailViewModel vm) return;
        if (string.IsNullOrEmpty(vm.Result.Caption)) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(vm.Result.Caption);
    }
}
