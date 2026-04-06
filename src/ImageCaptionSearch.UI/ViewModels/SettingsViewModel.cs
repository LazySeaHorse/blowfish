using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageCaptionSearch.Core.Interfaces;
using ImageCaptionSearch.Core.Models;

namespace ImageCaptionSearch.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly ILmStudioClient _lmClient;
    private readonly Action _onBack;

    [ObservableProperty] private string _baseUrl = "http://127.0.0.1:1234";
    [ObservableProperty] private string? _visionModelId;
    [ObservableProperty] private string? _embeddingModelId;
    [ObservableProperty] private int _maxConcurrency = 2;
    [ObservableProperty] private bool _faceDetectionEnabled;
    [ObservableProperty] private string? _faceDetectorModelPath;
    [ObservableProperty] private string? _faceRecognizerModelPath;
    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private string? _testResult;
    [ObservableProperty] private ObservableCollection<string> _availableModels = new();

    public SettingsViewModel(ISettingsService settingsService, ILmStudioClient lmClient, Action onBack)
    {
        _settingsService = settingsService;
        _lmClient = lmClient;
        _onBack = onBack;
        
        _ = LoadSettingsAsync();
        
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);
        RefreshModelsCommand = new AsyncRelayCommand(RefreshModelsAsync);
        SelectDetectorFileCommand = new AsyncRelayCommand(SelectDetectorFileAsync);
        SelectRecognizerFileCommand = new AsyncRelayCommand(SelectRecognizerFileAsync);
        NavigateBackCommand = new RelayCommand(onBack);
    }

    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand TestConnectionCommand { get; }
    public IAsyncRelayCommand RefreshModelsCommand { get; }
    public IAsyncRelayCommand SelectDetectorFileCommand { get; }
    public IAsyncRelayCommand SelectRecognizerFileCommand { get; }
    public IRelayCommand NavigateBackCommand { get; }

    private async Task LoadSettingsAsync()
    {
        var s = await _settingsService.GetSettingsAsync();
        BaseUrl = s.LmStudioBaseUrl;
        VisionModelId = s.VisionModelId;
        EmbeddingModelId = s.EmbeddingModelId;
        MaxConcurrency = s.MaxConcurrency;
        FaceDetectionEnabled = s.FaceDetectionEnabled;
        FaceDetectorModelPath = s.FaceDetectorModelPath;
        FaceRecognizerModelPath = s.FaceRecognizerModelPath;
        
        await RefreshModelsAsync();
    }

    private async Task SelectDetectorFileAsync()
    {
        // TODO: Open file picker
    }

    private async Task SelectRecognizerFileAsync()
    {
        // TODO: Open file picker
    }

    private async Task TestConnectionAsync()
    {
        IsTesting = true;
        TestResult = "Testing...";
        try
        {
            var success = await _lmClient.TestConnectionAsync(BaseUrl);
            TestResult = success ? "Success: Connected to LM Studio" : "Failed: Could not connect";
        }
        catch (Exception ex)
        {
            TestResult = $"Error: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    private async Task RefreshModelsAsync()
    {
        try
        {
            var models = await _lmClient.GetModelsAsync(BaseUrl);
            AvailableModels.Clear();
            foreach (var m in models) AvailableModels.Add(m.Id);
        }
        catch { /* Ignore if offline */ }
    }

    private async Task SaveAsync()
    {
        var s = await _settingsService.GetSettingsAsync();
        var updated = s with { 
            LmStudioBaseUrl = BaseUrl, 
            VisionModelId = VisionModelId, 
            EmbeddingModelId = EmbeddingModelId,
            MaxConcurrency = MaxConcurrency,
            FaceDetectionEnabled = FaceDetectionEnabled,
            FaceDetectorModelPath = FaceDetectorModelPath,
            FaceRecognizerModelPath = FaceRecognizerModelPath
        };
        await _settingsService.UpdateSettingsAsync(updated);
        _onBack();
    }
}
