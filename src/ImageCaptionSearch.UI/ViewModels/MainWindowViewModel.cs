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
        CurrentPage = new LibraryHomeViewModel(_serviceProvider.GetRequiredService<ILibraryRegistryService>(), OnLibrarySelected);
    }

    private LibraryDetailViewModel? _libraryDetailViewModel;

    private void OnLibrarySelected(Library library)
    {
        // Navigate to LibraryDetail
        Title = $"Blowfish - {library.DisplayName}";
        _libraryDetailViewModel = new LibraryDetailViewModel(library, _serviceProvider, OnImageSelected, NavigateToHome);
        CurrentPage = _libraryDetailViewModel;
    }

    private void OnImageSelected(SearchResultViewModel result)
    {
        if (_libraryDetailViewModel == null) return;
        CurrentPage = new ImageDetailViewModel(result, _libraryDetailViewModel.Library, _serviceProvider, OnSearchSimilar, () => 
        {
            if (_libraryDetailViewModel != null)
            {
                CurrentPage = _libraryDetailViewModel;
            }
            else
            {
                NavigateToHome();
            }
        });
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
        CurrentPage = new SettingsViewModel(
            _serviceProvider.GetRequiredService<ISettingsService>(),
            _serviceProvider.GetRequiredService<ILmStudioClient>(),
            NavigateToHome);
    }
}
