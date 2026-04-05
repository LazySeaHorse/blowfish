namespace ImageCaptionSearch.Core.Models;

public record EmbeddingRecord(
    string ParentId, // image_id or face_id
    string ModelName,
    int Dimension,
    float[] Vector,
    double VectorNorm,
    DateTime EmbeddedUtc
);
