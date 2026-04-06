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
    Task SaveCaptionAsync(Library library, CaptionRecord caption, CancellationToken ct = default);
    Task SaveEmbeddingAsync(Library library, EmbeddingRecord embedding, CancellationToken ct = default);
    Task<IReadOnlyList<EmbeddingRecord>> GetEmbeddingsAsync(Library library, string? modelId = null, CancellationToken ct = default);
    Task SaveFacesAsync(Library library, string imageId, IReadOnlyList<FaceDetectionResult> faces, string detectorModel, string recognizerModel, CancellationToken ct = default);
    Task<IReadOnlyList<FaceRecord>> GetFacesAsync(Library library, string imageId, CancellationToken ct = default);
    Task<IReadOnlyList<FaceEmbedding>> GetFaceEmbeddingsAsync(Library library, string? modelId = null, CancellationToken ct = default);

    // Job Management
    Task<IReadOnlyList<ProcessingJob>> GetActiveJobsAsync(Library library, CancellationToken ct = default);
    Task UpsertJobAsync(Library library, ProcessingJob job, CancellationToken ct = default);
    Task RemoveJobAsync(Library library, string imageId, CancellationToken ct = default);

    // Resets all image statuses to Pending and clears all derived data (captions, embeddings, faces)
    Task ResetLibraryIndexAsync(Library library, CancellationToken ct = default);
}



public record LibraryStatus(
    int TotalFiles,
    int IndexedCount,
    int PendingCount,
    int FailedCount,
    DateTime? LastScanUtc
);

