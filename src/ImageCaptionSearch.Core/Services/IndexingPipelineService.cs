using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using ImageCaptionSearch.Core.Interfaces;
using ImageCaptionSearch.Core.Models;
using Microsoft.Extensions.Logging;

namespace ImageCaptionSearch.Core.Services;

public class IndexingPipelineService : IIndexingPipelineService
{
    private readonly ILibraryRegistryService _libraryRegistry;
    private readonly ILibraryService _libraryService;
    private readonly IScanService _scanService;
    private readonly IThumbnailService _thumbnailService;
    private readonly ICaptionService _captionService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IFaceRecognitionService _faceRecognitionService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<IndexingPipelineService> _logger;

    private CancellationTokenSource? _cts;
    private IndexingProgress _progress = new(0, 0, 0, null, false, false);
    private int _processedCount;
    private int _failedCount;

    public event EventHandler<IndexingProgress>? ProgressChanged;

    public IndexingPipelineService(
        ILibraryRegistryService libraryRegistry,
        ILibraryService libraryService,
        IScanService scanService,
        IThumbnailService thumbnailService,
        ICaptionService captionService,
        IEmbeddingService embeddingService,
        IFaceRecognitionService faceRecognitionService,
        ISettingsService settingsService,
        ILogger<IndexingPipelineService> logger)
    {
        _libraryRegistry = libraryRegistry;
        _libraryService = libraryService;
        _scanService = scanService;
        _thumbnailService = thumbnailService;
        _captionService = captionService;
        _embeddingService = embeddingService;
        _faceRecognitionService = faceRecognitionService;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task StartAsync(Guid libraryId, CancellationToken ct = default)
    {
        if (_progress.IsRunning) return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;

        _processedCount = 0;
        _failedCount = 0;
        _progress = new IndexingProgress(0, 0, 0, null, true, false);
        NotifyProgress();
        
        _logger.LogInformation("Starting indexing for library {LibraryId}", libraryId);

        try
        {
            var library = await _libraryRegistry.GetLibraryByIdAsync(libraryId, token);
            if (library == null) throw new InvalidOperationException("Library not found.");

            var settings = await _settingsService.GetSettingsAsync(token);

            // 1. Scan phase
            var existing = await _libraryService.GetImagesAsync(library, token);
            var scanResult = await _scanService.ScanAsync(library.RootPath, existing, token);

            if (scanResult.MissingIds.Any())
            {
                await _libraryService.MarkMissingAsync(library, scanResult.MissingIds, token);
            }

            // Combine new, modified, and any pending items
            var pendingFromDb = existing.Where(i => i.Status != ProcessingState.Completed && i.Status != ProcessingState.Failed && !i.IsMissing);
            
            // Startup Recovery: Check active jobs
            var activeJobs = await _libraryService.GetActiveJobsAsync(library, token);
            var maxRetries = 3;

            var itemsToProcess = scanResult.NewItems
                .Concat(scanResult.ModifiedItems)
                .Concat(pendingFromDb)
                .GroupBy(i => i.Id)
                .Select(g => g.First())
                .ToList();

            // Filter out items that have failed too many times
            var toFail = new List<ImageRecord>();
            var filteredItems = new List<ImageRecord>();
            foreach (var item in itemsToProcess)
            {
                var job = activeJobs.FirstOrDefault(j => j.ImageId == item.Id);
                if (job != null && job.RetryCount >= maxRetries)
                {
                    _logger.LogWarning("Item {ImageId} has failed {RetryCount} times. Marking as Failed.", item.Id, job.RetryCount);
                    toFail.Add(item with { Status = ProcessingState.Failed, LastError = "Max retries exceeded during previous runs." });
                }
                else
                {
                    filteredItems.Add(item);
                }
            }

            if (toFail.Any())
            {
                await _libraryService.UpsertImagesAsync(library, toFail, token);
                foreach (var f in toFail) await _libraryService.RemoveJobAsync(library, f.Id, token);
            }

            itemsToProcess = filteredItems;

            _progress = _progress with { TotalCount = itemsToProcess.Count };
            NotifyProgress();

            if (itemsToProcess.Count == 0) return;

            // 2. Define stages
            
            // Stage 1: Thumbnailing & Metadata
            var thumbnailBlock = new TransformBlock<PipelineItem, PipelineItem>(async item =>
            {
                if (item.Image.Status >= ProcessingState.ThumbnailGenerated) return item;
                
                try
                {
                    UpdateCurrentFile(item.Image.FileName);
                    var fullPath = Path.Combine(library.RootPath, item.Image.RelativePath);
                    var dims = await _thumbnailService.GetImageDimensionsAsync(fullPath, token);
                    var thumbRelPath = $"thumbnails/{item.Image.Id}.jpg";
                    var thumbFullPath = Path.Combine(library.RootPath, ".imagecaptionsearch", thumbRelPath);
                    
                    await _thumbnailService.GenerateThumbnailAsync(fullPath, thumbFullPath, 256, token);

                    var updated = item.Image with 
                    { 
                        Width = dims.Width, 
                        Height = dims.Height, 
                        ThumbnailRelPath = thumbRelPath,
                        Status = ProcessingState.ThumbnailGenerated,
                        LastProcessedUtc = DateTime.UtcNow
                    };
                    await _libraryService.UpsertImagesAsync(library, new[] { updated }, token);
                    return item with { Image = updated };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Thumbnailing failed for {ImageId}", item.Image.Id);
                    await HandleErrorAsync(library, item.Image, ex, token);
                    return item with { Image = item.Image with { Status = ProcessingState.Failed } };
                }
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 4, CancellationToken = token });

            // Stage 2: Captioning
            var captionBlock = new TransformBlock<PipelineItem, PipelineItem>(async item =>
            {
                if (item.Image.Status != ProcessingState.ThumbnailGenerated) return item;
                if (string.IsNullOrEmpty(settings.VisionModelId)) 
                {
                    // Skip AI if no model selected
                    return item with { Image = item.Image with { Status = ProcessingState.Captioned } };
                }

                try
                {
                    UpdateCurrentFile(item.Image.FileName);
                    var caption = await _captionService.GenerateCaptionAsync(library.RootPath, item.Image, settings, token);
                    await _libraryService.SaveCaptionAsync(library, caption, token);
                    
                    var updated = item.Image with { Status = ProcessingState.Captioned };
                    await _libraryService.UpsertImagesAsync(library, new[] { updated }, token);
                    return item with { Image = updated, CaptionText = caption.Caption, HasHuman = caption.HasHuman };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Captioning failed for {ImageId}", item.Image.Id);
                    await HandleErrorAsync(library, item.Image, ex, token);
                    return item with { Image = item.Image with { Status = ProcessingState.Failed } };
                }
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = settings.MaxConcurrency, CancellationToken = token });

            // Stage 3: Embedding
            var embeddingBlock = new TransformBlock<PipelineItem, PipelineItem>(async item =>
            {
                if (item.Image.Status != ProcessingState.Captioned) return item;

                if (string.IsNullOrEmpty(settings.EmbeddingModelId))
                {
                    return item;
                }

                try
                {
                    UpdateCurrentFile(item.Image.FileName);
                    var text = item.CaptionText;
                    if (!string.IsNullOrEmpty(text))
                    {
                        var embedding = await _embeddingService.GenerateEmbeddingAsync(item.Image.Id, text, settings, false, token);
                        await _libraryService.SaveEmbeddingAsync(library, embedding, token);
                    }
                    return item;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Embedding failed for {ImageId}", item.Image.Id);
                    await HandleErrorAsync(library, item.Image, ex, token);
                    return item with { Image = item.Image with { Status = ProcessingState.Failed } };
                }
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = settings.MaxConcurrency, CancellationToken = token });

            // Stage 4: Face Recognition
            var faceBlock = new ActionBlock<PipelineItem>(async item =>
            {
                if (item.Image.Status == ProcessingState.Failed)
                {
                    Interlocked.Increment(ref _failedCount);
                    UpdateProgress();
                    return;
                }

                if (settings.FaceDetectionEnabled && item.HasHuman && _faceRecognitionService.IsAvailable())
                {
                    try
                    {
                        UpdateCurrentFile(item.Image.FileName + " (faces)");
                        var fullPath = Path.Combine(library.RootPath, item.Image.RelativePath);
                        var faces = await _faceRecognitionService.DetectAndEmbedFacesAsync(fullPath, token);
                        if (faces.Count > 0)
                        {
                            await _libraryService.SaveFacesAsync(library, item.Image.Id, faces, settings.FaceDetectorModelPath!, settings.FaceRecognizerModelPath!, token);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Face processing failed for {ImageId}, but continuing pipeline.", item.Image.Id);
                    }
                }

                await CompleteItemAsync(library, item.Image, token);
                _progress = _progress with { ProcessedCount = _processedCount, FailedCount = _failedCount };
                NotifyProgress();

            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = Math.Max(1, settings.MaxConcurrency / 2), CancellationToken = token });



            // 3. Link stages
            var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };
            thumbnailBlock.LinkTo(captionBlock, linkOptions);
            captionBlock.LinkTo(embeddingBlock, linkOptions);
            embeddingBlock.LinkTo(faceBlock, linkOptions);

            // 4. Feed the pipeline
            foreach (var item in itemsToProcess)
            {
                if (token.IsCancellationRequested) break;
                
                // Track job start/retry
                var existingJob = activeJobs.FirstOrDefault(j => j.ImageId == item.Id);
                var retryCount = (existingJob?.RetryCount ?? 0) + 1;
                await _libraryService.UpsertJobAsync(library, new ProcessingJob(item.Id, retryCount, item.Status.ToString(), DateTime.UtcNow), token);

                await thumbnailBlock.SendAsync(new PipelineItem(item), token);
            }

            thumbnailBlock.Complete();
            await faceBlock.Completion;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        finally
        {
            _progress = _progress with { IsRunning = false, CurrentFileName = null };
            NotifyProgress();
        }
    }

    private async Task CompleteItemAsync(Library library, ImageRecord image, CancellationToken ct)
    {
        var final = image with { Status = ProcessingState.Completed, LastProcessedUtc = DateTime.UtcNow, LastError = null };
        await _libraryService.UpsertImagesAsync(library, new[] { final }, ct);
        await _libraryService.RemoveJobAsync(library, image.Id, ct);
        Interlocked.Increment(ref _processedCount);
    }

    private async Task HandleErrorAsync(Library library, ImageRecord image, Exception ex, CancellationToken ct)
    {
        var failed = image with { Status = ProcessingState.Failed, LastError = ex.Message, LastProcessedUtc = DateTime.UtcNow };
        await _libraryService.UpsertImagesAsync(library, new[] { failed }, ct);
        await _libraryService.RemoveJobAsync(library, image.Id, ct);
    }

    private void UpdateCurrentFile(string fileName)
    {
        _progress = _progress with { CurrentFileName = fileName };
        NotifyProgress();
    }

    private void UpdateProgress()
    {
        _progress = _progress with { ProcessedCount = _processedCount, FailedCount = _failedCount };
        NotifyProgress();
    }

    public Task PauseAsync() 
    { 
        _progress = _progress with { IsPaused = true }; 
        NotifyProgress();
        return Task.CompletedTask; 
    }

    public Task ResumeAsync() 
    { 
        _progress = _progress with { IsPaused = false }; 
        NotifyProgress();
        return Task.CompletedTask; 
    }

    public Task CancelAsync() 
    { 
        _cts?.Cancel(); 
        return Task.CompletedTask; 
    }

    public IndexingProgress GetProgress() => _progress;

    private void NotifyProgress() => ProgressChanged?.Invoke(this, _progress);

    private sealed record PipelineItem(ImageRecord Image, string? CaptionText = null, bool HasHuman = false);
}
