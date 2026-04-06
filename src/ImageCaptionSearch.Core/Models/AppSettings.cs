namespace ImageCaptionSearch.Core.Models;

public record AppSettings(
    string LmStudioBaseUrl = "http://127.0.0.1:1234",
    string? VisionModelId = null,
    string? EmbeddingModelId = null,
    int CaptionTimeoutSeconds = 300,
    int EmbeddingTimeoutSeconds = 120,
    int MaxConcurrency = 2,
    int MaxRetries = 3,
    bool FaceDetectionEnabled = false,
    string? FaceDetectorModelPath = null,
    string? FaceRecognizerModelPath = null
);
