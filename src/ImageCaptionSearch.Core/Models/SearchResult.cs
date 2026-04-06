namespace ImageCaptionSearch.Core.Models;

public record SearchQuery(
    string QueryText,
    SearchMode Mode,
    bool? FilterHasHuman = null,
    int Limit = 100,
    int Offset = 0
);

public record SearchResultItem(
     string ImageId,
     string RelativePath,
     string FileName,
     string? CaptionSnippet,
     bool HasHuman,
     double Score,
     string? ThumbnailRelPath,
     string? LastError = null
);
