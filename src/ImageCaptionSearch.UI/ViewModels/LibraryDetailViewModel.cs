using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageCaptionSearch.Core.Interfaces;
using ImageCaptionSearch.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ImageCaptionSearch.UI.ViewModels;

public partial class LibraryDetailViewModel : ViewModelBase, IDisposable
{
    private readonly Library _library;
    public Library Library => _library;
    private readonly ISearchService _searchService;
    private readonly IIndexingPipelineService _indexingPipeline;
    private readonly IFaceRecognitionService _faceRecognition;
    private readonly ISettingsService _settingsService;
    private readonly Action _onBack;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private SearchMode _searchMode = SearchMode.Caption;

    [ObservableProperty]
    private bool _isSemanticSearchSelected = false;

    [ObservableProperty]
    private bool _isCaptionSearchSelected = true;

    [ObservableProperty]
    private bool? _filterHasHuman = null;

    [ObservableProperty]
    private ObservableCollection<SearchResultViewModel> _results = new();

    [ObservableProperty]
    private IndexingProgress _progress;
    
    [ObservableProperty]
    private bool _showFaceWarning;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private SearchResultViewModel? _selectedResult;

    public string LibraryName => _library.DisplayName;

    private readonly Action<SearchResultViewModel> _onImageSelected;

    public LibraryDetailViewModel(Library library, IServiceProvider serviceProvider, Action<SearchResultViewModel> onImageSelected, Action onBack)
    {
        _library = library;
        _searchService = serviceProvider.GetRequiredService<ISearchService>();
        _indexingPipeline = serviceProvider.GetRequiredService<IIndexingPipelineService>();
        _faceRecognition = serviceProvider.GetRequiredService<IFaceRecognitionService>();
        _settingsService = serviceProvider.GetRequiredService<ISettingsService>();
        _onBack = onBack;
        _onImageSelected = onImageSelected;
        
        _ = CheckFaceServiceAsync();

        _indexingPipeline.ProgressChanged += OnProgressChanged;
        _progress = _indexingPipeline.GetProgress();

        SearchCommand = new AsyncRelayCommand(ExecuteSearchAsync);
        NavigateBackCommand = new RelayCommand(onBack);
        StartIndexingCommand = new AsyncRelayCommand(StartIndexingAsync);
        PauseIndexingCommand = new AsyncRelayCommand(() => _indexingPipeline.PauseAsync());
        ResumeIndexingCommand = new AsyncRelayCommand(() => _indexingPipeline.ResumeAsync());
        CancelIndexingCommand = new AsyncRelayCommand(() => _indexingPipeline.CancelAsync());
        OpenDetailCommand = new RelayCommand<SearchResultViewModel>(OpenDetail);

        // Initial search to show recent/all
        _ = ExecuteSearchAsync();
    }

    private void OpenDetail(SearchResultViewModel? result)
    {
        if (result == null) return;
        _onImageSelected(result);
    }

    private void OnProgressChanged(object? sender, IndexingProgress e)
    {
        Progress = e;
    }

    public IAsyncRelayCommand SearchCommand { get; }
    public IRelayCommand NavigateBackCommand { get; }
    public IAsyncRelayCommand StartIndexingCommand { get; }
    public IAsyncRelayCommand PauseIndexingCommand { get; }
    public IAsyncRelayCommand ResumeIndexingCommand { get; }
    public IAsyncRelayCommand CancelIndexingCommand { get; }
    public IRelayCommand<SearchResultViewModel> OpenDetailCommand { get; }

    partial void OnIsSemanticSearchSelectedChanged(bool value)
    {
        if (value)
        {
            IsCaptionSearchSelected = false;
            SearchMode = SearchMode.Semantic;
        }
    }

    partial void OnIsCaptionSearchSelectedChanged(bool value)
    {
        if (value)
        {
            IsSemanticSearchSelected = false;
            SearchMode = SearchMode.Caption;
        }
    }

    partial void OnSearchModeChanged(SearchMode value)
    {
        IsSemanticSearchSelected = value == SearchMode.Semantic;
        IsCaptionSearchSelected = value == SearchMode.Caption;
    }

    partial void OnSelectedResultChanged(SearchResultViewModel? value)
    {
        if (value != null)
        {
            OpenDetail(value);
            SelectedResult = null; // Clear so we can select same item again
        }
    }

    private async Task ExecuteSearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        IsSearching = true;
        try
        {
            var query = new SearchQuery(
                SearchText,
                SearchMode,
                FilterHasHuman,
                Limit: 100,
                Offset: 0
            );

            var items = await _searchService.SearchAsync(_library.Id, query, token);
            
            Results.Clear();
            foreach (var item in items)
            {
                Results.Add(new SearchResultViewModel(item, _library.RootPath));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Handle error
            Console.WriteLine(ex.Message);
        }
        finally
        {
            IsSearching = false;
        }
    }

    private async Task StartIndexingAsync()
    {
        try
        {
            await _indexingPipeline.StartAsync(_library.Id);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    public async Task FindSimilarImagesAsync(string imageId)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        IsSearching = true;
        try
        {
            SearchText = "Similar Images";
            var items = await _searchService.FindSimilarImagesAsync(_library.Id, imageId, 100, token);
            Results.Clear();
            foreach (var item in items)
            {
                Results.Add(new SearchResultViewModel(item, _library.RootPath));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
        finally
        {
            IsSearching = false;
        }
    }

    public async Task FindSimilarFacesAsync(string faceId)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        IsSearching = true;
        try
        {
            SearchText = "Similar Faces";
            var items = await _searchService.FindSimilarFacesAsync(_library.Id, faceId, 100, token);
            Results.Clear();
            foreach (var item in items)
            {
                Results.Add(new SearchResultViewModel(item, _library.RootPath));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
        finally
        {
            IsSearching = false;
        }
    }

    private async Task CheckFaceServiceAsync()
    {
        var settings = await _settingsService.GetSettingsAsync();
        if (settings.FaceDetectionEnabled && !_faceRecognition.IsAvailable())
        {
            ShowFaceWarning = true;
        }
    }

    public void Cleanup()
    {
        _indexingPipeline.ProgressChanged -= OnProgressChanged;
    }

    public void Dispose()
    {
        Cleanup();
        _searchCts?.Cancel();
        _searchCts?.Dispose();
    }
}

public partial class SearchResultViewModel : ViewModelBase
{
    private readonly SearchResultItem _item;
    private readonly string _libraryRoot;

    public string ImageId => _item.ImageId;
    public string ImagePath { get; }
    public string FileName => _item.FileName;
    public string? Caption => _item.CaptionSnippet;
    public bool HasHuman => _item.HasHuman;
    public double Score => _item.Score;
    public string? ThumbnailPath { get; }
    public string? LastError => _item.LastError;

    public SearchResultViewModel(SearchResultItem item, string libraryRoot)
    {
        _item = item;
        _libraryRoot = libraryRoot;
        ImagePath = System.IO.Path.Combine(libraryRoot, item.RelativePath);
        
        if (!string.IsNullOrEmpty(item.ThumbnailRelPath))
        {
            ThumbnailPath = System.IO.Path.Combine(libraryRoot, ".imagecaptionsearch", item.ThumbnailRelPath);
        }
    }
}
