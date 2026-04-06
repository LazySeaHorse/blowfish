using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ImageCaptionSearch.UI.ViewModels;

namespace ImageCaptionSearch.UI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void OnSelectDetectorClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        var file = await PickOnnxFileAsync("Select Face Detector ONNX Model");
        if (file != null)
            vm.FaceDetectorModelPath = file;
    }

    private async void OnSelectRecognizerClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        var file = await PickOnnxFileAsync("Select Face Recognizer ONNX Model");
        if (file != null)
            vm.FaceRecognizerModelPath = file;
    }

    private async Task<string?> PickOnnxFileAsync(string title)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("ONNX Model") { Patterns = new[] { "*.onnx" } },
                FilePickerFileTypes.All,
            }
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }
}
