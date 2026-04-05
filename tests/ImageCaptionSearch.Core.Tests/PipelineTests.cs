using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageCaptionSearch.Core.Interfaces;
using ImageCaptionSearch.Core.Models;
using ImageCaptionSearch.Core.Services;
using Moq;
using Xunit;

namespace ImageCaptionSearch.Core.Tests;

public class PipelineTests
{
    private readonly Mock<ILibraryRegistryService> _mockRegistry;
    private readonly Mock<ILibraryService> _mockLibrary;
    private readonly Mock<IScanService> _mockScan;
    private readonly Mock<IThumbnailService> _mockThumbnail;
    private readonly Mock<ICaptionService> _mockCaption;
    private readonly Mock<IEmbeddingService> _mockEmbedding;
    private readonly Mock<ISettingsService> _mockSettings;
    private readonly IndexingPipelineService _pipeline;
    private readonly Library _testLib;

    public PipelineTests()
    {
        _mockRegistry = new Mock<ILibraryRegistryService>();
        _mockLibrary = new Mock<ILibraryService>();
        _mockScan = new Mock<IScanService>();
        _mockThumbnail = new Mock<IThumbnailService>();
        _mockCaption = new Mock<ICaptionService>();
        _mockEmbedding = new Mock<IEmbeddingService>();
        _mockSettings = new Mock<ISettingsService>();

        _pipeline = new IndexingPipelineService(
            _mockRegistry.Object,
            _mockLibrary.Object,
            _mockScan.Object,
            _mockThumbnail.Object,
            _mockCaption.Object,
            _mockEmbedding.Object,
            _mockSettings.Object
        );

        _testLib = new Library(Guid.NewGuid(), "path", "test", DateTime.UtcNow, null);
    }

    [Fact]
    public async Task StartAsyncShouldCompleteWhenNoItemsFound()
    {
        // Arrange
        _mockRegistry.Setup(r => r.GetLibraryByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testLib);
        _mockSettings.Setup(s => s.GetSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppSettings());
        _mockLibrary.Setup(l => l.GetImagesAsync(It.IsAny<Library>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ImageRecord>());
        _mockScan.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<ImageRecord>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScanResult(new List<ImageRecord>(), new List<ImageRecord>(), new List<string>()));

        // Act
        await _pipeline.StartAsync(_testLib.Id, CancellationToken.None);

        // Assert
        var progress = _pipeline.GetProgress();
        Assert.False(progress.IsRunning);
        Assert.Equal(0, progress.TotalCount);
    }

    [Fact]
    public async Task StartAsyncShouldProcessSingleItem()
    {
        // Arrange
        var image = new ImageRecord("id1", "1.jpg", "1.jpg", ".jpg", 10, DateTime.UtcNow, DateTime.UtcNow, ProcessingState.Pending);
        _mockRegistry.Setup(r => r.GetLibraryByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testLib);
        _mockSettings.Setup(s => s.GetSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppSettings { MaxConcurrency = 1 });
        _mockLibrary.Setup(l => l.GetImagesAsync(It.IsAny<Library>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ImageRecord>());
        _mockScan.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<ImageRecord>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScanResult(new List<ImageRecord> { image }, new List<ImageRecord>(), new List<string>()));
        _mockLibrary.Setup(l => l.UpsertImagesAsync(It.IsAny<Library>(), It.IsAny<IEnumerable<ImageRecord>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockLibrary.Setup(l => l.SaveEmbeddingAsync(It.IsAny<Library>(), It.IsAny<EmbeddingRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockThumbnail.Setup(t => t.GetImageDimensionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageDimensions(100, 100));
        _mockThumbnail.Setup(t => t.GenerateThumbnailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockCaption.Setup(c => c.GenerateCaptionAsync(It.IsAny<string>(), It.IsAny<ImageRecord>(), It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CaptionRecord("id1", "cat", "{}", false, "v", "1", DateTime.UtcNow));
        _mockEmbedding.Setup(e => e.GenerateEmbeddingAsync("id1", "cat", It.IsAny<AppSettings>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingRecord("id1", "em", 2, new float[] { 0, 0 }, 0, DateTime.UtcNow));

        // Act
        await _pipeline.StartAsync(_testLib.Id, CancellationToken.None);

        // Assert
        _mockThumbnail.Verify(t => t.GenerateThumbnailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        var progress = _pipeline.GetProgress();
        Assert.Equal(1, progress.ProcessedCount);
    }
}
