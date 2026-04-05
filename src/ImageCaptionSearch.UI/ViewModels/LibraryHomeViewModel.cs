using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageCaptionSearch.Core.Interfaces;
using ImageCaptionSearch.Core.Models;

namespace ImageCaptionSearch.UI.ViewModels;

public partial class LibraryHomeViewModel : ViewModelBase
{
    private readonly ILibraryRegistryService _libraryRegistry;
    private readonly Action<Library> _onLibrarySelected;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<LibraryViewModel> Libraries { get; } = new();

    public IAsyncRelayCommand LoadLibrariesCommand { get; }
    public IAsyncRelayCommand AddLibraryCommand { get; }

    public LibraryHomeViewModel(ILibraryRegistryService libraryRegistry, Action<Library> onLibrarySelected)
    {
        _libraryRegistry = libraryRegistry;
        _onLibrarySelected = onLibrarySelected;

        LoadLibrariesCommand = new AsyncRelayCommand(LoadLibrariesInternalAsync);
        AddLibraryCommand = new AsyncRelayCommand(AddLibraryInternalAsync);
    }

    private async Task LoadLibrariesInternalAsync()
    {
        IsLoading = true;
        try
        {
            var libs = await _libraryRegistry.GetLibrariesAsync();
            Libraries.Clear();
            foreach (var lib in libs)
            {
                Libraries.Add(new LibraryViewModel(lib, OpenLibraryCommand, RemoveLibraryCommand));
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenLibrary(LibraryViewModel libraryVM)
    {
        _onLibrarySelected(libraryVM.Library);
    }

    [RelayCommand]
    private async Task RemoveLibrary(LibraryViewModel libraryVM)
    {
        // TODO: Show confirmation dialog
        await _libraryRegistry.RemoveLibraryAsync(libraryVM.Library.Id);
        await LoadLibrariesInternalAsync();
    }

    private async Task AddLibraryInternalAsync()
    {
        // This will be called from the View which will use StorageProvider to pick a folder
        // For now this is a placeholder to be triggered by the View.
    }
    
    public async Task AddLibraryFromPathAsync(string path, string displayName)
    {
        try
        {
            var library = await _libraryRegistry.AddLibraryAsync(path, displayName);
            await LoadLibrariesInternalAsync();
            _onLibrarySelected(library);
        }
        catch (Exception ex)
        {
            // TODO: Error reporting
            Console.WriteLine(ex.Message);
        }
    }
}

public partial class LibraryViewModel : ViewModelBase
{
    public Library Library { get; }
    public string Name => Library.DisplayName;
    public string Path => Library.RootPath;
    
    public ICommand OpenCommand { get; }
    public ICommand RemoveCommand { get; }

    public LibraryViewModel(Library library, ICommand openCommand, ICommand removeCommand)
    {
        Library = library;
        OpenCommand = openCommand;
        RemoveCommand = removeCommand;
    }
}
