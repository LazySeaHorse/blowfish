using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageCaptionSearch.Core.Interfaces;
using ImageCaptionSearch.Core.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace ImageCaptionSearch.Core.Services;

public class FaceRecognitionService : IFaceRecognitionService
{
    private readonly ISettingsService _settingsService;
    private InferenceSession? _detectorSession;
    private InferenceSession? _recognizerSession;
    private AppSettings? _cachedSettings;

    public FaceRecognitionService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public bool IsAvailable()
    {
        var settings = Task.Run(() => _settingsService.GetSettingsAsync()).Result;
        return settings.FaceDetectionEnabled &&
               !string.IsNullOrWhiteSpace(settings.FaceDetectorModelPath) &&
               File.Exists(settings.FaceDetectorModelPath) &&
               !string.IsNullOrWhiteSpace(settings.FaceRecognizerModelPath) &&
               File.Exists(settings.FaceRecognizerModelPath);
    }

    public async Task<IReadOnlyList<FaceDetectionResult>> DetectAndEmbedFacesAsync(string imagePath, CancellationToken ct = default)
    {
        var settings = await _settingsService.GetSettingsAsync(ct);
        if (!settings.FaceDetectionEnabled) return Array.Empty<FaceDetectionResult>();

        try
        {
            await EnsureSessionsAsync(settings);
            
            if (_detectorSession == null || _recognizerSession == null)
            {
                return Array.Empty<FaceDetectionResult>();
            }

            using var input = File.OpenRead(imagePath);
            using var codec = SKCodec.Create(input);
            if (codec == null) return Array.Empty<FaceDetectionResult>();
            
            using var originalBitmap = SKBitmap.Decode(codec);
            if (originalBitmap == null) return Array.Empty<FaceDetectionResult>();

            // 1. Detect faces
            var faces = await DetectFacesInternalAsync(originalBitmap, ct);
            if (faces.Count == 0) return Array.Empty<FaceDetectionResult>();

            // 2. Embed faces
            var results = new List<FaceDetectionResult>();
            for (int i = 0; i < faces.Count; i++)
            {
                var face = faces[i];
                var vector = await EmbedFaceInternalAsync(originalBitmap, face, ct);
                if (vector != null)
                {
                    results.Add(new FaceDetectionResult(
                        i,
                        face.Left,
                        face.Top,
                        face.Width,
                        face.Height,
                        vector
                    ));
                }
            }

            return results;
        }
        catch (Exception)
        {
            // Fail gracefully as per spec 518
            return Array.Empty<FaceDetectionResult>();
        }
    }

    private async Task EnsureSessionsAsync(AppSettings settings)
    {
        if (_cachedSettings != null && 
            _cachedSettings.FaceDetectorModelPath == settings.FaceDetectorModelPath && 
            _cachedSettings.FaceRecognizerModelPath == settings.FaceRecognizerModelPath &&
            _detectorSession != null && _recognizerSession != null)
        {
            return;
        }

        _detectorSession?.Dispose();
        _recognizerSession?.Dispose();
        _detectorSession = null;
        _recognizerSession = null;

        if (File.Exists(settings.FaceDetectorModelPath))
        {
            _detectorSession = new InferenceSession(settings.FaceDetectorModelPath);
        }

        if (File.Exists(settings.FaceRecognizerModelPath))
        {
            _recognizerSession = new InferenceSession(settings.FaceRecognizerModelPath);
        }

        _cachedSettings = settings;
    }

    private Task<List<SKRect>> DetectFacesInternalAsync(SKBitmap bitmap, CancellationToken ct)
    {
        // For MVP, we use a simplified UltraFace-like detection logic
        // or just return an empty list if we don't want to implement full post-processing yet.
        // Actually, let's try to implement a basic UltraFace 320/640 detector.
        
        return Task.Run(() =>
        {
            if (_detectorSession == null) return new List<SKRect>();

            // UltraFace constant: 320x240 or 640x480
            // We'll check the session input metadata
            var inputMeta = _detectorSession.InputMetadata.Values.First();
            int width = inputMeta.Dimensions[3];
            int height = inputMeta.Dimensions[2];

            using var resized = bitmap.Resize(new SKImageInfo(width, height), SKFilterQuality.Medium);
            var tensor = CreateTensorFromBitmap(resized);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_detectorSession.InputMetadata.Keys.First(), tensor)
            };

            using var results = _detectorSession.Run(inputs);
            
            // UltraFace outputs: boxes and scores
            // scores: (1, N, 2) - face/no-face
            // boxes: (1, N, 4) - [x_min, y_min, x_max, y_max]
            
            var scores = results.ElementAt(0).AsEnumerable<float>().ToArray();
            var boxes = results.ElementAt(1).AsEnumerable<float>().ToArray();

            return PostProcessUltraFace(scores, boxes, bitmap.Width, bitmap.Height);
        }, ct);
    }

    private Task<float[]> EmbedFaceInternalAsync(SKBitmap bitmap, SKRect faceBox, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            if (_recognizerSession == null) return null;

            // Crop face
            var rect = faceBox;
            // Pad slightly
            float pad = rect.Width * 0.1f;
            var cropRect = new SKRect(
                Math.Max(0, rect.Left - pad),
                Math.Max(0, rect.Top - pad),
                Math.Min(bitmap.Width, rect.Right + pad),
                Math.Min(bitmap.Height, rect.Bottom + pad)
            );

            using var faceBitmap = new SKBitmap((int)cropRect.Width, (int)cropRect.Height);
            using var canvas = new SKCanvas(faceBitmap);
            canvas.DrawBitmap(bitmap, cropRect, new SKRect(0, 0, cropRect.Width, cropRect.Height));

            // MobileFaceNet input: 112x112
            var inputMeta = _recognizerSession.InputMetadata.Values.First();
            int w = inputMeta.Dimensions[3];
            int h = inputMeta.Dimensions[2];

            using var resized = faceBitmap.Resize(new SKImageInfo(w, h), SKFilterQuality.Medium);
            var tensor = CreateTensorFromBitmap(resized, true); // Normalize for MobileFaceNet

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_recognizerSession.InputMetadata.Keys.First(), tensor)
            };

            using var results = _recognizerSession.Run(inputs);
            var vector = results.First().AsEnumerable<float>().ToArray();
            
            // Normalize vector
            float norm = 0;
            foreach (var v in vector) norm += v * v;
            norm = (float)Math.Sqrt(norm);
            if (norm > 1e-6)
            {
                for (int i = 0; i < vector.Length; i++) vector[i] /= norm;
            }

            return vector;
        }, ct);
    }

    private DenseTensor<float> CreateTensorFromBitmap(SKBitmap bitmap, bool normalize = false)
    {
        int w = bitmap.Width;
        int h = bitmap.Height;
        var tensor = new DenseTensor<float>(new[] { 1, 3, h, w });

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (normalize)
                {
                    // MobileFaceNet normalization (usually (x - 127.5) / 128)
                    tensor[0, 0, y, x] = (color.Red - 127.5f) / 128.0f;
                    tensor[0, 1, y, x] = (color.Green - 127.5f) / 128.0f;
                    tensor[0, 2, y, x] = (color.Blue - 127.5f) / 128.0f;
                }
                else
                {
                    // UltraFace normalization (usuall (x - 127) / 128)
                    tensor[0, 0, y, x] = (color.Red - 127f) / 128.0f;
                    tensor[0, 1, y, x] = (color.Green - 127f) / 128.0f;
                    tensor[0, 2, y, x] = (color.Blue - 127f) / 128.0f;
                }
            }
        }
        return tensor;
    }

    private List<SKRect> PostProcessUltraFace(float[] scores, float[] boxes, int imageWidth, int imageHeight)
    {
        // This is a simplified NMS and box extraction for UltraFace
        // UltraFace has 2 outputs: scores (N, 2) and boxes (N, 4)
        // We'll skip complex NMS for MVP and just return boxes above threshold
        
        var detections = new List<(SKRect rect, float score)>();
        int numDetections = scores.Length / 2;

        for (int i = 0; i < numDetections; i++)
        {
            float score = scores[i * 2 + 1]; // Face score
            if (score > 0.8f)
            {
                float x1 = boxes[i * 4] * imageWidth;
                float y1 = boxes[i * 4 + 1] * imageHeight;
                float x2 = boxes[i * 4 + 2] * imageWidth;
                float y2 = boxes[i * 4 + 3] * imageHeight;
                
                detections.Add((new SKRect(x1, y1, x2, y2), score));
            }
        }

        // Simple NMS
        var sorted = detections.OrderByDescending(d => d.score).ToList();
        var results = new List<SKRect>();
        while (sorted.Count > 0)
        {
            var best = sorted[0];
            results.Add(best.rect);
            sorted.RemoveAt(0);
            sorted.RemoveAll(d => CalculateIoU(best.rect, d.rect) > 0.45f);
        }

        return results;
    }

    private float CalculateIoU(SKRect a, SKRect b)
    {
        float areaA = a.Width * a.Height;
        float areaB = b.Width * b.Height;
        
        var intersection = SKRect.Intersect(a, b);
        if (intersection.IsEmpty) return 0;
        
        float areaI = intersection.Width * intersection.Height;
        return areaI / (areaA + areaB - areaI);
    }

    public void Dispose()
    {
        _detectorSession?.Dispose();
        _recognizerSession?.Dispose();
    }
}
