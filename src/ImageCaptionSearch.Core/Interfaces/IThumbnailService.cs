namespace ImageCaptionSearch.Core.Interfaces;

public record ImageDimensions(int Width, int Height);

public interface IThumbnailService
{
    Task<ImageDimensions> GetImageDimensionsAsync(string imagePath, CancellationToken ct = default);
    Task GenerateThumbnailAsync(string imagePath, string thumbPath, int targetSize = 256, CancellationToken ct = default);
}
