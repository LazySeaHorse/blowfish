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
using Microsoft.Extensions.Logging;

namespace ImageCaptionSearch.UI.ViewModels;

public partial class LibraryHomeViewModel : ViewModelBase
{
    private readonly ILibraryRegistryService _libraryRegistry;
    private readonly ILogger<LibraryHomeViewModel> _logger;
    // bool = autoStartIndexing: true when the library was just created
    private readonly Action<Library, bool> _onLibrarySelected;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<LibraryViewModel> Libraries { get; } = new();

    public IAsyncRelayCommand LoadLibrariesCommand { get; }
    public IAsyncRelayCommand AddLibraryCommand { get; }

    public LibraryHomeViewModel(ILibraryRegistryService libraryRegistry, ILogger<LibraryHomeViewModel> logger, Action<Library, bool> onLibrarySelected)
    {
        _libraryRegistry = libraryRegistry;
        _logger = logger;
        _onLibrarySelected = onLibrarySelected;

        LoadLibrariesCommand = new AsyncRelayCommand(LoadLibrariesInternalAsync);
        AddLibraryCommand = new AsyncRelayCommand(AddLibraryInternalAsync);
        
        _ = LoadLibrariesInternalAsync();
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load libraries.");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenLibrary(LibraryViewModel libraryVM)
    {
        _onLibrarySelected(libraryVM.Library, false);
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
            _onLibrarySelected(library, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add library from path {Path}", path);
            ErrorMessage = ex is InvalidOperationException or DirectoryNotFoundException or UnauthorizedAccessException
                ? ex.Message
                : "Failed to add folder as a library.";
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
