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

public class SearchServiceTests : IDisposable
{
    private readonly Mock<ILibraryRegistryService> _mockRegistry;
    private readonly Mock<ILibraryService> _mockLibrary;
    private readonly Mock<IEmbeddingService> _mockEmbedding;
    private readonly Mock<ISettingsService> _mockSettings;
    private readonly SearchService _searchService;
    private readonly string _libraryRoot;
    private readonly Library _testLib;

    public SearchServiceTests()
    {
        _mockRegistry = new Mock<ILibraryRegistryService>();
        _mockLibrary = new Mock<ILibraryService>();
        _mockEmbedding = new Mock<IEmbeddingService>();
        _mockSettings = new Mock<ISettingsService>();
        _searchService = new SearchService(_mockRegistry.Object, _mockLibrary.Object, _mockEmbedding.Object, _mockSettings.Object);
        _libraryRoot = Path.Combine(Path.GetTempPath(), "SearchTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_libraryRoot);
        _testLib = new Library(Guid.NewGuid(), _libraryRoot, "Test", DateTime.UtcNow, null);
        _mockRegistry.Setup(r => r.GetLibraryByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testLib);
    }

    [Fact]
    public async Task SemanticSearchShouldReturnRankedResults()
    {
        // This test will verify the logic flow of semantic search.
        // Since SearchService.FetchMetadataBatchAsync uses a real SQLite file, 
        // we'll need to initialize a valid but empty catalog.db.
        
        var libraryService = new LibraryService();
        await libraryService.InitializeLibraryAsync(_testLib, CancellationToken.None);

        // Arrange
        var query = new SearchQuery("cat", SearchMode.Semantic, Limit: 10, Offset: 0);
        var settings = new AppSettings { EmbeddingModelId = "model" };
        _mockSettings.Setup(s => s.GetSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        
        var qv = new float[] { 1.0f, 0.0f };
        var queryRecord = new EmbeddingRecord("query", "model", 2, qv, 1.0, DateTime.UtcNow);
        _mockEmbedding.Setup(e => e.GenerateEmbeddingAsync("query", "cat", settings, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(queryRecord);

        var embeddings = new List<EmbeddingRecord>
        {
            new EmbeddingRecord("img1", "model", 2, new float[] { 0.0f, 1.0f }, 1.0, DateTime.UtcNow),
            new EmbeddingRecord("img2", "model", 2, new float[] { 1.0f, 0.0f }, 1.0, DateTime.UtcNow) // Best match
        };
        _mockLibrary.Setup(l => l.GetEmbeddingsAsync(_testLib, "model", It.IsAny<CancellationToken>()))
            .ReturnsAsync(embeddings);

        // We need to add images to the real catalog.db so FetchMetadataBatchAsync finds them.
        await libraryService.UpsertImagesAsync(_testLib, new[] 
        { 
            new ImageRecord("img1", "1.jpg", "1.jpg", ".jpg", 10, DateTime.UtcNow, DateTime.UtcNow, ProcessingState.Completed),
            new ImageRecord("img2", "2.jpg", "2.jpg", ".jpg", 10, DateTime.UtcNow, DateTime.UtcNow, ProcessingState.Completed)
        }, CancellationToken.None);

        // Act
        var results = await _searchService.SearchAsync(_testLib.Id, query, CancellationToken.None);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal("img2", results[0].ImageId); // img2 matches qv=[1,0] perfectly
        Assert.Equal(1.0, results[0].Score, 5);
        Assert.Equal(0.0, results[1].Score, 5);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_libraryRoot))
                Directory.Delete(_libraryRoot, true);
        }
    }
}
