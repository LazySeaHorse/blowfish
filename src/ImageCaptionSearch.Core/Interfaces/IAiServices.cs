using ImageCaptionSearch.Core.Models;

namespace ImageCaptionSearch.Core.Interfaces;

public interface ICaptionService
{
    Task<CaptionRecord> GenerateCaptionAsync(string libraryRoot, ImageRecord image, AppSettings settings, CancellationToken ct = default);
}

public interface IEmbeddingService
{
    Task<EmbeddingRecord> GenerateEmbeddingAsync(string parentId, string text, AppSettings settings, CancellationToken ct = default);
}
