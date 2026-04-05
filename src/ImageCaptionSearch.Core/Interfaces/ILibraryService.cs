using ImageCaptionSearch.Core.Models;

namespace ImageCaptionSearch.Core.Interfaces;

public interface ILibraryService
{
    Task InitializeLibraryAsync(Library library, CancellationToken ct = default);
    Task<LibraryStatus> GetLibraryStatusAsync(Library library, CancellationToken ct = default);
}

public record LibraryStatus(
    int TotalFiles,
    int IndexedCount,
    int PendingCount,
    int FailedCount,
    DateTime? LastScanUtc
);
