using ImageCaptionSearch.Core.Models;

namespace ImageCaptionSearch.Core.Interfaces;

public record ScanResult(
    IReadOnlyList<ImageRecord> NewItems,
    IReadOnlyList<ImageRecord> ModifiedItems,
    IReadOnlyList<string> MissingIds
);

public interface IScanService
{
    Task<ScanResult> ScanAsync(string rootPath, IReadOnlyList<ImageRecord> existingImages, CancellationToken ct = default);
}
