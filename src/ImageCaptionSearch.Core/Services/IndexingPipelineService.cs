using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using ImageCaptionSearch.Core.Interfaces;
using ImageCaptionSearch.Core.Models;

namespace ImageCaptionSearch.Core.Services;

public class IndexingPipelineService : IIndexingPipelineService
{
    private readonly ILibraryRegistryService _libraryRegistry;
    private readonly ILibraryService _libraryService;
    private readonly IScanService _scanService;
    private readonly IThumbnailService _thumbnailService;

    private CancellationTokenSource? _cts;
    private IndexingProgress _progress = new(0, 0, 0, null, false, false);
    private int _processedCount;
    private int _failedCount;

    public event EventHandler<IndexingProgress>? ProgressChanged;

    public IndexingPipelineService(
        ILibraryRegistryService libraryRegistry,
        ILibraryService libraryService,
        IScanService scanService,
        IThumbnailService thumbnailService)
    {
        _libraryRegistry = libraryRegistry;
        _libraryService = libraryService;
        _scanService = scanService;
        _thumbnailService = thumbnailService;
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

        try
        {
            var library = await _libraryRegistry.GetLibraryByIdAsync(libraryId, token);
            if (library == null) throw new InvalidOperationException("Library not found.");

            // 1. Scan phase
            var existing = await _libraryService.GetImagesAsync(library, token);
            var scanResult = await _scanService.ScanAsync(library.RootPath, existing, token);

            // Update missing items immediately
            if (scanResult.MissingIds.Any())
            {
                await _libraryService.MarkMissingAsync(library, scanResult.MissingIds, token);
            }

            var itemsToProcess = scanResult.NewItems.Concat(scanResult.ModifiedItems).ToList();
            _progress = _progress with { TotalCount = itemsToProcess.Count };
            NotifyProgress();

            if (!itemsToProcess.Any())
            {
                return;
            }

            // 2. Thumbnailing phase (using TPL Dataflow)
            var thumbnailBlock = new ActionBlock<ImageRecord>(async item =>
            {
                try
                {
                    _progress = _progress with { CurrentFileName = item.FileName };
                    NotifyProgress();

                    var fullPath = Path.Combine(library.RootPath, item.RelativePath);
                    
                    // Extract dimensions
                    var dims = await _thumbnailService.GetImageDimensionsAsync(fullPath, token);

                    // Generate thumbnail
                    var thumbRelPath = $"thumbnails/{item.Id}.jpg";
                    var thumbFullPath = Path.Combine(library.RootPath, ".imagecaptionsearch", thumbRelPath);
                    await _thumbnailService.GenerateThumbnailAsync(fullPath, thumbFullPath, 256, token);

                    // Update record
                    var updated = item with 
                    { 
                        Width = dims.Width, 
                        Height = dims.Height, 
                        ThumbnailRelPath = thumbRelPath,
                        Status = ProcessingState.ThumbnailGenerated,
                        LastProcessedUtc = DateTime.UtcNow
                    };

                    await _libraryService.UpsertImagesAsync(library, new[] { updated }, token);
                    
                    Interlocked.Increment(ref _processedCount);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _failedCount);
                    var failed = item with { Status = ProcessingState.Failed, LastError = ex.Message };
                    await _libraryService.UpsertImagesAsync(library, new[] { failed }, token);
                }
                finally
                {
                    _progress = _progress with { ProcessedCount = _processedCount, FailedCount = _failedCount };
                    NotifyProgress();
                }
            }, new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 4, // Concurrency for local IO/Image processing
                CancellationToken = token
            });

            foreach (var item in itemsToProcess)
            {
                if (token.IsCancellationRequested) break;
                await thumbnailBlock.SendAsync(item, token);
            }

            thumbnailBlock.Complete();
            await thumbnailBlock.Completion;
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
}
