using System;

namespace ImageCaptionSearch.Core.Models;

public record ProcessingJob(
    string ImageId,
    int RetryCount,
    string PipelineState,
    DateTime UpdatedUtc
);
