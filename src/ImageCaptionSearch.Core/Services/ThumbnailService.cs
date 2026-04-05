using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageCaptionSearch.Core.Interfaces;
using SkiaSharp;

namespace ImageCaptionSearch.Core.Services;

public class ThumbnailService : IThumbnailService
{
    public Task<ImageDimensions> GetImageDimensionsAsync(string imagePath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var input = File.OpenRead(imagePath);
            using var codec = SKCodec.Create(input);
            if (codec == null) throw new InvalidOperationException($"Failed to load image codec for {imagePath}");

            // EXIF orientation might swap width/height
            var origin = codec.EncodedOrigin;
            var width = codec.Info.Width;
            var height = codec.Info.Height;

            if (origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom)
            {
                return new ImageDimensions(height, width);
            }

            return new ImageDimensions(width, height);
        }, ct);
    }

    public Task GenerateThumbnailAsync(string imagePath, string thumbPath, int targetSize = 256, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var input = File.OpenRead(imagePath);
            using var codec = SKCodec.Create(input);
            if (codec == null) throw new InvalidOperationException($"Failed to decode image {imagePath}");

            var origin = codec.EncodedOrigin;
            
            using var bitmap = SKBitmap.Decode(codec);
            if (bitmap == null) throw new InvalidOperationException($"Failed to decode bitmap for {imagePath}");

            // Calculate scaled size maintaining aspect ratio
            int width, height;
            if (bitmap.Width > bitmap.Height)
            {
                width = targetSize;
                height = (int)(bitmap.Height * (float)targetSize / bitmap.Width);
            }
            else
            {
                height = targetSize;
                width = (int)(bitmap.Width * (float)targetSize / bitmap.Height);
            }

            using var resized = bitmap.Resize(new SKImageInfo(width, height), SKFilterQuality.Medium);
            if (resized == null) throw new InvalidOperationException("Failed to resize bitmap.");

            using var oriented = ApplyOrientation(resized, origin);

            using var image = SKImage.FromBitmap(oriented);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 80);
            
            var dir = Path.GetDirectoryName(thumbPath);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            using var output = File.OpenWrite(thumbPath);
            data.SaveTo(output);
        }, ct);
    }

    private static SKBitmap ApplyOrientation(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        switch (origin)
        {
            case SKEncodedOrigin.BottomRight: // 180 degrees
                return Rotate(bitmap, 180);
            case SKEncodedOrigin.RightTop: // 90 degrees CW
                return Rotate(bitmap, 90);
            case SKEncodedOrigin.LeftBottom: // 270 degrees CW
                return Rotate(bitmap, 270);
            default:
                // For other orientations (flips), we just return the original for now (MVP simplification)
                return bitmap;
        }
    }

    private static SKBitmap Rotate(SKBitmap bitmap, int degrees)
    {
        var rotated = new SKBitmap(
            degrees == 90 || degrees == 270 ? bitmap.Height : bitmap.Width,
            degrees == 90 || degrees == 270 ? bitmap.Width : bitmap.Height);

        using var canvas = new SKCanvas(rotated);
        
        if (degrees == 90)
        {
            canvas.Translate(bitmap.Height, 0);
            canvas.RotateDegrees(90);
        }
        else if (degrees == 180)
        {
            canvas.Translate(bitmap.Width, bitmap.Height);
            canvas.RotateDegrees(180);
        }
        else if (degrees == 270)
        {
            canvas.Translate(0, bitmap.Width);
            canvas.RotateDegrees(270);
        }

        canvas.DrawBitmap(bitmap, 0, 0);
        return rotated;
    }
}
