namespace ImageCaptionSearch.Core.Interfaces;

public record IndexingProgress(
    int TotalCount,
    int ProcessedCount,
    int FailedCount,
    string? CurrentFileName,
    bool IsRunning,
    bool IsPaused
);

public interface IIndexingPipelineService
{
    event EventHandler<IndexingProgress>? ProgressChanged;
    Task StartAsync(Guid libraryId, CancellationToken ct = default);
    Task PauseAsync();
    Task ResumeAsync();
    Task CancelAsync();
    IndexingProgress GetProgress();
}
