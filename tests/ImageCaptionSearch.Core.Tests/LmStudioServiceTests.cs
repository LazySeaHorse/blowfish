using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImageCaptionSearch.Core.Interfaces;
using ImageCaptionSearch.Core.Models;
using ImageCaptionSearch.Core.Services;
using Moq;
using Moq.Protected;
using Xunit;

namespace ImageCaptionSearch.Core.Tests;

public class LmStudioServiceTests
{
    private readonly Mock<ILmStudioClient> _mockClient;
    private readonly CaptionService _captionService;
    private readonly EmbeddingService _embeddingService;
    private readonly AppSettings _settings;

    public LmStudioServiceTests()
    {
        _mockClient = new Mock<ILmStudioClient>();
        _captionService = new CaptionService(_mockClient.Object);
        _embeddingService = new EmbeddingService(_mockClient.Object);
        _settings = new AppSettings
        {
            LmStudioBaseUrl = "http://localhost:1234",
            VisionModelId = "vision-model",
            EmbeddingModelId = "embedding-model"
        };
    }

    [Fact]
    public async Task GenerateCaptionAsyncShouldReturnParsedCaptionRecordWhenJsonIsValid()
    {
        // Arrange
        var image = new ImageRecord("id1", "test.jpg", "test.jpg", ".jpg", 100, DateTime.UtcNow, DateTime.UtcNow, ProcessingState.Pending);
        var jsonResponse = "{\"caption\": \"A beautiful sunset\", \"has_human\": false}";
        _mockClient.Setup(c => c.GetChatCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jsonResponse);

        // We need a dummy image file for CaptionService to read.
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".jpg");
        using (var bitmap = new SkiaSharp.SKBitmap(10, 10))
        using (var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 100))
        using (var stream = File.OpenWrite(tempPath))
        {
            data.SaveTo(stream);
        }

        try
        {
            // Act
            var result = await _captionService.GenerateCaptionAsync(Path.GetDirectoryName(tempPath)!, image with { RelativePath = Path.GetFileName(tempPath) }, _settings, CancellationToken.None);

            // Assert
            Assert.Equal("A beautiful sunset", result.Caption);
            Assert.False(result.HasHuman);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task EmbeddingServiceShouldCalculateNorm()
    {
        // Arrange
        var vector = new float[] { 3.0f, 4.0f }; // Norm should be 5.0
        _mockClient.Setup(c => c.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(vector);

        // Act
        var result = await _embeddingService.GenerateEmbeddingAsync("parent", "text", _settings, false, CancellationToken.None);

        // Assert
        Assert.Equal(5.0, result.VectorNorm, 5);
        Assert.Equal(vector, result.Vector);
    }

    [Fact]
    public async Task CaptionServiceShouldHandleParsingErrorsGracefully()
    {
        // Arrange
        var image = new ImageRecord("id1", "test.jpg", "test.jpg", ".jpg", 100, DateTime.UtcNow, DateTime.UtcNow, ProcessingState.Pending);
        var jsonResponse = "{ \"something_else\": \"invalid\" }";
        _mockClient.Setup(c => c.GetChatCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jsonResponse);

        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".jpg");
        using (var bitmap = new SkiaSharp.SKBitmap(10, 10))
        using (var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 100))
        using (var stream = File.OpenWrite(tempPath))
        {
            data.SaveTo(stream);
        }

        try
        {
            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _captionService.GenerateCaptionAsync(Path.GetDirectoryName(tempPath)!, image with { RelativePath = Path.GetFileName(tempPath) }, _settings, CancellationToken.None));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
