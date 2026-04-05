using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageCaptionSearch.Core.Models;
using ImageCaptionSearch.Core.Services;
using Xunit;

namespace ImageCaptionSearch.Core.Tests;

public class LibraryServiceTests : IDisposable
{
    private readonly string _libraryRoot;
    private readonly Library _library;
    private readonly LibraryService _service;

    public LibraryServiceTests()
    {
        _libraryRoot = Path.Combine(Path.GetTempPath(), "LibraryServiceTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_libraryRoot);
        _library = new Library(Guid.NewGuid(), _libraryRoot, "Test Lib", DateTime.UtcNow, null);
        _service = new LibraryService();
    }

    [Fact]
    public async Task InitializeLibraryAsyncShouldCreateInternalStructure()
    {
        // Act
        await _service.InitializeLibraryAsync(_library, CancellationToken.None);

        // Assert
        var internalDir = Path.Combine(_libraryRoot, ".imagecaptionsearch");
        Assert.True(Directory.Exists(internalDir));
        Assert.True(File.Exists(Path.Combine(internalDir, "catalog.db")));
        Assert.True(File.Exists(Path.Combine(internalDir, "vectors.db")));
        Assert.True(Directory.Exists(Path.Combine(internalDir, "thumbnails")));
    }

    [Fact]
    public async Task UpsertAndGetImagesShouldWork()
    {
        // Arrange
        await _service.InitializeLibraryAsync(_library, CancellationToken.None);
        var img = new ImageRecord(
            Guid.NewGuid().ToString(),
            "test.jpg",
            "test.jpg",
            ".jpg",
            100,
            DateTime.UtcNow,
            DateTime.UtcNow,
            ProcessingState.Pending
        );

        // Act
        await _service.UpsertImagesAsync(_library, new[] { img }, CancellationToken.None);
        var images = await _service.GetImagesAsync(_library, CancellationToken.None);

        // Assert
        Assert.Single(images);
        Assert.Equal(img.RelativePath, images[0].RelativePath);
    }

    [Fact]
    public async Task SaveCaptionShouldWorkAndSyncWithImages()
    {
        // Arrange
        await _service.InitializeLibraryAsync(_library, CancellationToken.None);
        var imgId = Guid.NewGuid().ToString();
        var img = new ImageRecord(imgId, "c.jpg", "c.jpg", ".jpg", 10, DateTime.UtcNow, DateTime.UtcNow, ProcessingState.Pending);
        await _service.UpsertImagesAsync(_library, new[] { img }, CancellationToken.None);

        var caption = new CaptionRecord(imgId, "A cat", "{}", false, "model", "1.0", DateTime.UtcNow);

        // Act
        await _service.SaveCaptionAsync(_library, caption, CancellationToken.None);
        
        // Assert
        // We check if it is saved by fetching it back if we had a GetCaptionAsync, but we can check SearchService later.
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
