using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ImageCaptionSearch.Core.Interfaces;
using ImageCaptionSearch.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ImageCaptionSearch.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILibraryRegistryService _libraryRegistry;

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private string _title = "Blowfish";

    public MainWindowViewModel(IServiceProvider serviceProvider, ILibraryRegistryService libraryRegistry)
    {
        _serviceProvider = serviceProvider;
        _libraryRegistry = libraryRegistry;

        NavigateToHomeCommand = new RelayCommand(NavigateToHome);
        NavigateToHome();
    }

    public IRelayCommand NavigateToHomeCommand { get; }

    public void NavigateToHome()
    {
        _libraryDetailViewModel?.Dispose();
        _libraryDetailViewModel = null;
        CurrentPage = ActivatorUtilities.CreateInstance<LibraryHomeViewModel>(_serviceProvider, (Action<Library, bool>)OnLibrarySelected);
    }

    private LibraryDetailViewModel? _libraryDetailViewModel;

    private void OnLibrarySelected(Library library, bool autoStartIndexing)
    {
        Title = $"Blowfish - {library.DisplayName}";
        _libraryDetailViewModel = ActivatorUtilities.CreateInstance<LibraryDetailViewModel>(_serviceProvider, library, (Action<SearchResultViewModel>)OnImageSelected, (Action)NavigateToHome);
        CurrentPage = _libraryDetailViewModel;

        if (autoStartIndexing)
            _ = _libraryDetailViewModel.StartIndexingAsync();
    }

    private void OnImageSelected(SearchResultViewModel result)
    {
        if (_libraryDetailViewModel == null) return;
        CurrentPage = ActivatorUtilities.CreateInstance<ImageDetailViewModel>(_serviceProvider, result, _libraryDetailViewModel.Library, (Action<Library, string, SearchMode>)OnSearchSimilar, (Action)(() => 
        {
            if (_libraryDetailViewModel != null)
            {
                CurrentPage = _libraryDetailViewModel;
            }
            else
            {
                NavigateToHome();
            }
        }));
    }

    private void OnSearchSimilar(Library library, string id, SearchMode mode)
    {
        if (_libraryDetailViewModel != null)
        {
            CurrentPage = _libraryDetailViewModel;
            if (mode == SearchMode.Semantic)
            {
                _ = _libraryDetailViewModel.FindSimilarImagesAsync(id);
            }
            else if (mode == SearchMode.FaceSimilarity)
            {
                _ = _libraryDetailViewModel.FindSimilarFacesAsync(id);
            }
        }
    }

    [RelayCommand]
    public void NavigateToSettings()
    {
        CurrentPage = ActivatorUtilities.CreateInstance<SettingsViewModel>(_serviceProvider, (Action)NavigateToHome);
    }
}
