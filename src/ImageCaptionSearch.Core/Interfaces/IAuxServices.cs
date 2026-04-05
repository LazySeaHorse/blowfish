using ImageCaptionSearch.Core.Models;

namespace ImageCaptionSearch.Core.Interfaces;

public interface ISettingsService
{
    Task<AppSettings> GetSettingsAsync(CancellationToken ct = default);
    Task UpdateSettingsAsync(AppSettings settings, CancellationToken ct = default);
}

public record FaceDetectionResult(
    int FaceIndex,
    double BBoxX,
    double BBoxY,
    double BBoxWidth,
    double BBoxHeight,
    float[] Vector
);

public interface IFaceRecognitionService
{
    bool IsAvailable();
    Task<IReadOnlyList<FaceDetectionResult>> DetectAndEmbedFacesAsync(string imagePath, CancellationToken ct = default);
}
