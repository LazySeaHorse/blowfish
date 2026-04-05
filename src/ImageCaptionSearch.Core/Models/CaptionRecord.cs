namespace ImageCaptionSearch.Core.Models;

public record CaptionRecord(
    string ImageId,
    string Caption,
    string RawJson,
    bool HasHuman,
    string VisionModel,
    string PromptVersion,
    DateTime CaptionedUtc
);
