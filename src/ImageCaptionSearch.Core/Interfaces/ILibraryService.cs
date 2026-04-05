using ImageCaptionSearch.Core.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ImageCaptionSearch.Core.Interfaces;

public interface ILibraryService
{
    Task InitializeLibraryAsync(Library library, CancellationToken ct = default);
    Task<LibraryStatus> GetLibraryStatusAsync(Library library, CancellationToken ct = default);
    Task<IReadOnlyList<ImageRecord>> GetImagesAsync(Library library, CancellationToken ct = default);
    Task UpsertImagesAsync(Library library, IEnumerable<ImageRecord> images, CancellationToken ct = default);
    Task MarkMissingAsync(Library library, IEnumerable<string> imageIds, CancellationToken ct = default);
}

public record LibraryStatus(
    int TotalFiles,
    int IndexedCount,
    int PendingCount,
    int FailedCount,
    DateTime? LastScanUtc
);

