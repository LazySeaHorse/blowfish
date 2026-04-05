namespace ImageCaptionSearch.Core.Models;

public record FaceRecord(
    string Id,
    string ImageId,
    int FaceIndex,
    double BBoxX,
    double BBoxY,
    double BBoxWidth,
    double BBoxHeight,
    string DetectorModel,
    string RecognizerModel,
    DateTime CreatedUtc
);
 public record FaceEmbedding(
    string FaceId,
    string ModelName,
    int Dimension,
    float[] Vector,
    double VectorNorm
 );
