namespace ImageCaptionSearch.Core.Models;

public record Library(
    Guid Id,
    string RootPath,
    string DisplayName,
    DateTime CreatedUtc,
    DateTime? UpdatedUtc,
    string? VisionModelId = null,
    string? EmbeddingModelId = null,
    string? PromptVersion = null
);
