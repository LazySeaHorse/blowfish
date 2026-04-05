namespace ImageCaptionSearch.Core.Models;

public enum ProcessingState
{
    Pending,
    Loading,
    ThumbnailGenerated,
    Captioning,
    Embedding,
    FaceDetection,
    Completed,
    Failed,
    Missing
}
