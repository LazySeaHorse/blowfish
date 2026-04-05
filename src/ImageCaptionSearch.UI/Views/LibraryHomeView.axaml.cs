using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ImageCaptionSearch.UI.ViewModels;
using System.IO;
using System.Threading.Tasks;
using System;

namespace ImageCaptionSearch.UI.Views;

public partial class LibraryHomeView : UserControl
{
    public LibraryHomeView()
    {
        InitializeComponent();
    }

    private async void OnAddLibraryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LibraryHomeViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Library Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var folder = folders[0];
            var path = folder.Path.LocalPath;
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) name = path;
            
            await vm.AddLibraryFromPathAsync(path, name);
        }
    }
}
