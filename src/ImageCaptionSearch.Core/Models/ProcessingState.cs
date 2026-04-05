namespace ImageCaptionSearch.Core.Models;

public enum ProcessingState
{
    Pending,
    ThumbnailGenerated,
    Captioned,
    Completed,
    Failed,
    Missing
}
