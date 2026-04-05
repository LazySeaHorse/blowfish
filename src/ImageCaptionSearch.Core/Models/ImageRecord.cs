namespace ImageCaptionSearch.Core.Models;

public record ImageRecord(
    string Id,
    string RelativePath,
    string FileName,
    string Extension,
    long SizeBytes,
    DateTime ModifiedUtc,
    DateTime DiscoveredUtc,
    ProcessingState Status,
    bool IsMissing = false,
    int? Width = null,
    int? Height = null,
    DateTime? LastProcessedUtc = null,
    string? ThumbnailRelPath = null,
    string? LastError = null
);
