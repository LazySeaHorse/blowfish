using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageCaptionSearch.Core.Interfaces;
using ImageCaptionSearch.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ImageCaptionSearch.UI.ViewModels;

public partial class ImageDetailViewModel : ViewModelBase
{
    private readonly SearchResultViewModel _result;
    private readonly Library _library;
    private readonly ILibraryService _libraryService;
    private readonly ISearchService _searchService;
    private readonly ILogger<ImageDetailViewModel> _logger;
    private readonly Action _onBack;
    private readonly Action<Library, string, SearchMode> _onSearchSimilar;

    public SearchResultViewModel Result => _result;

    [ObservableProperty]
    private int _faceCount;

    [ObservableProperty]
    private ObservableCollection<FaceRecord> _faces = new();

    public ImageDetailViewModel(
        SearchResultViewModel result, 
        Library library,
        IServiceProvider serviceProvider,
        ILogger<ImageDetailViewModel> logger,
        Action<Library, string, SearchMode> onSearchSimilar,
        Action onBack)
    {
        _result = result;
        _library = library;
        _libraryService = serviceProvider.GetRequiredService<ILibraryService>();
        _searchService = serviceProvider.GetRequiredService<ISearchService>();
        _logger = logger;
        _onSearchSimilar = onSearchSimilar;
        _onBack = onBack;

        NavigateBackCommand = new RelayCommand(onBack);
        OpenImageCommand = new RelayCommand(OpenImage);
        ShowInExplorerCommand = new RelayCommand(ShowInExplorer);
        FindSimilarImagesCommand = new AsyncRelayCommand(FindSimilarImagesAsync);
        FindSimilarFacesCommand = new AsyncRelayCommand<string>(FindSimilarFacesAsync);

        _ = LoadFaceDataAsync();
    }

    public IRelayCommand NavigateBackCommand { get; }
    public IRelayCommand OpenImageCommand { get; }
    public IRelayCommand ShowInExplorerCommand { get; }
    public IAsyncRelayCommand FindSimilarImagesCommand { get; }
    public IAsyncRelayCommand<string> FindSimilarFacesCommand { get; }

    private async Task LoadFaceDataAsync()
    {
        try
        {
            var faces = await _libraryService.GetFacesAsync(_library, _result.ImageId);
            FaceCount = faces.Count;
            Faces.Clear();
            foreach (var f in faces) Faces.Add(f);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load face data for {ImageId}", _result.ImageId);
        }
    }

    private void OpenImage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_result.ImagePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open image {Path}", _result.ImagePath);
        }
    }

    private void ShowInExplorer()
    {
        try
        {
            var argument = "/select, \"" + _result.ImagePath + "\"";
            Process.Start("explorer.exe", argument);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show in explorer: {Path}", _result.ImagePath);
        }
    }

    private async Task FindSimilarImagesAsync()
    {
        _onSearchSimilar(_library, _result.ImageId, SearchMode.Semantic);
    }

    private async Task FindSimilarFacesAsync(string? faceId)
    {
        if (string.IsNullOrEmpty(faceId))
        {
            // If faceId is null, use the first face if available
            var firstFace = Faces.FirstOrDefault();
            if (firstFace != null) faceId = firstFace.Id;
        }

        if (!string.IsNullOrEmpty(faceId))
        {
            _onSearchSimilar(_library, faceId, SearchMode.FaceSimilarity); 
        }
    }
}
