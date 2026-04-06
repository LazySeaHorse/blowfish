using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImageCaptionSearch.Core.Interfaces;
using ImageCaptionSearch.Core.Models;
using SkiaSharp;

namespace ImageCaptionSearch.Core.Services;

public class CaptionService : ICaptionService
{
    private readonly ILmStudioClient _client;
    private const string PromptVersion = "1.0";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public CaptionService(ILmStudioClient client)
    {
        _client = client;
    }

    public async Task<CaptionRecord> GenerateCaptionAsync(string libraryRoot, ImageRecord image, AppSettings settings, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(settings.VisionModelId)) 
            throw new InvalidOperationException("Vision model not selected in settings.");

        var fullPath = Path.Combine(libraryRoot, image.RelativePath);
        var imageBytes = await ReadAndResizeImageAsync(fullPath, 512, ct); 

        var prompt = "Describe the attached image in detail. Output ONLY JSON with exactly two keys: " +
                    "\"caption\" (a searchable description covering subjects, setting, colors, mood, and notable details) " +
                    "and \"has_human\" (boolean, true if any real human or human body part is visible). " +
                    "No markdown, no code fences, no commentary.";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(settings.CaptionTimeoutSeconds));

        var rawResponse = await _client.GetChatCompletionAsync(
            settings.LmStudioBaseUrl,
            settings.VisionModelId,
            prompt,
            imageBytes,
            false,
            cts.Token);

        try 
        {
            var parsed = JsonSerializer.Deserialize<LmCaptionResult>(rawResponse, JsonOptions);
            if (string.IsNullOrWhiteSpace(parsed?.Caption)) 
                throw new InvalidOperationException("Caption content was empty.");

            return new CaptionRecord(
                image.Id,
                parsed.Caption.Trim(),
                rawResponse,
                parsed.HasHuman,
                settings.VisionModelId,
                PromptVersion,
                DateTime.UtcNow
            );
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse LM Studio response as JSON: {ex.Message}. Raw: {rawResponse}");
        }
    }

    private static async Task<byte[]> ReadAndResizeImageAsync(string path, int maxSize, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            using var fileStream = File.OpenRead(path);
            using var codec = SKCodec.Create(fileStream);
            if (codec == null) throw new InvalidOperationException("Failed to load image codec.");

            using var bitmap = SKBitmap.Decode(codec);
            if (bitmap == null) throw new InvalidOperationException("Failed to decode bitmap.");

            // CreateRotated returns a new bitmap only when rotation is needed,
            // so it is always safe to dispose independently of `bitmap`.
            using var rotated = CreateRotatedBitmap(bitmap, codec.EncodedOrigin);
            var working = rotated ?? bitmap;

            if (working.Width > maxSize || working.Height > maxSize)
            {
                int width, height;
                if (working.Width > working.Height)
                {
                    width = maxSize;
                    height = (int)(working.Height * (float)maxSize / working.Width);
                }
                else
                {
                    height = maxSize;
                    width = (int)(working.Width * (float)maxSize / working.Height);
                }

                using var resized = working.Resize(new SKImageInfo(width, height), SKFilterQuality.Medium);
                using var image = SKImage.FromBitmap(resized);
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
                return data.ToArray();
            }

            using var originalImage = SKImage.FromBitmap(working);
            using var originalData = originalImage.Encode(SKEncodedImageFormat.Jpeg, 85);
            return originalData.ToArray();
        }, ct);
    }

    /// <summary>Returns a new rotated bitmap when the EXIF orientation requires it, or null if no rotation is needed.</summary>
    private static SKBitmap? CreateRotatedBitmap(SKBitmap bitmap, SKEncodedOrigin origin) => origin switch
    {
        SKEncodedOrigin.BottomRight => Rotate(bitmap, 180),
        SKEncodedOrigin.RightTop    => Rotate(bitmap, 90),
        SKEncodedOrigin.LeftBottom  => Rotate(bitmap, 270),
        _                           => null,
    };

    private static SKBitmap Rotate(SKBitmap bitmap, int degrees)
    {
        var rotated = new SKBitmap(
            degrees == 90 || degrees == 270 ? bitmap.Height : bitmap.Width,
            degrees == 90 || degrees == 270 ? bitmap.Width : bitmap.Height);

        using var canvas = new SKCanvas(rotated);
        if (degrees == 90) { canvas.Translate(bitmap.Height, 0); canvas.RotateDegrees(90); }
        else if (degrees == 180) { canvas.Translate(bitmap.Width, bitmap.Height); canvas.RotateDegrees(180); }
        else if (degrees == 270) { canvas.Translate(0, bitmap.Width); canvas.RotateDegrees(270); }
        canvas.DrawBitmap(bitmap, 0, 0);
        return rotated;
    }

    private sealed class LmCaptionResult
    {
        public string? Caption { get; set; }
        public bool HasHuman { get; set; }
    }
}
