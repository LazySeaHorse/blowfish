using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageCaptionSearch.Core.Models;
using ImageCaptionSearch.Core.Services;
using Xunit;

namespace ImageCaptionSearch.Core.Tests;

public class ScanServiceTests : IDisposable
{
    private readonly string _tempPath;
    private readonly ScanService _scanService;

    public ScanServiceTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "ScanServiceTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempPath);
        _scanService = new ScanService();
    }

    [Fact]
    public async Task ScanAsyncShouldFindNewImages()
    {
        // Arrange
        File.WriteAllBytes(Path.Combine(_tempPath, "test1.jpg"), new byte[10]);
        File.WriteAllBytes(Path.Combine(_tempPath, "test2.png"), new byte[20]);
        File.WriteAllBytes(Path.Combine(_tempPath, "test.txt"), new byte[5]); // Should be ignored

        // Act
        var result = await _scanService.ScanAsync(_tempPath, Array.Empty<ImageRecord>(), CancellationToken.None);

        // Assert
        Assert.Equal(2, result.NewItems.Count);
        Assert.Empty(result.ModifiedItems);
        Assert.Empty(result.MissingIds);
        Assert.Contains(result.NewItems, i => i.FileName == "test1.jpg");
        Assert.Contains(result.NewItems, i => i.FileName == "test2.png");
    }

    [Fact]
    public async Task ScanAsyncShouldIgnoreInternalFolder()
    {
        // Arrange
        var internalDir = Path.Combine(_tempPath, ".imagecaptionsearch");
        Directory.CreateDirectory(internalDir);
        File.WriteAllBytes(Path.Combine(internalDir, "catalog.db"), new byte[10]);
        File.WriteAllBytes(Path.Combine(_tempPath, "real.jpg"), new byte[10]);

        // Act
        var result = await _scanService.ScanAsync(_tempPath, Array.Empty<ImageRecord>(), CancellationToken.None);

        // Assert
        Assert.Single(result.NewItems);
        Assert.Equal("real.jpg", result.NewItems[0].FileName);
    }

    [Fact]
    public async Task ScanAsyncShouldDetectModifiedFiles()
    {
        // Arrange
        var filePath = Path.Combine(_tempPath, "mod.jpg");
        File.WriteAllBytes(filePath, new byte[10]);
        var lastWrite = File.GetLastWriteTimeUtc(filePath);

        var existing = new ImageRecord(
            "id1",
            "mod.jpg",
            "mod.jpg",
            ".jpg",
            10,
            lastWrite.AddSeconds(-10), // Older than actual
            DateTime.UtcNow,
            ProcessingState.Completed
        );

        // Act
        var result = await _scanService.ScanAsync(_tempPath, new[] { existing }, CancellationToken.None);

        // Assert
        Assert.Empty(result.NewItems);
        Assert.Single(result.ModifiedItems);
        Assert.Equal("id1", result.ModifiedItems[0].Id);
        Assert.Equal(ProcessingState.Pending, result.ModifiedItems[0].Status);
    }

    [Fact]
    public async Task ScanAsyncShouldDetectMissingFiles()
    {
        // Arrange
        var existing = new ImageRecord(
            "id-missing",
            "missing.jpg",
            "missing.jpg",
            ".jpg",
            10,
            DateTime.UtcNow,
            DateTime.UtcNow,
            ProcessingState.Completed
        );

        // Act
        var result = await _scanService.ScanAsync(_tempPath, new[] { existing }, CancellationToken.None);

        // Assert
        Assert.Empty(result.NewItems);
        Assert.Empty(result.ModifiedItems);
        Assert.Single(result.MissingIds);
        Assert.Equal("id-missing", result.MissingIds[0]);
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
            try
            {
                if (Directory.Exists(_tempPath))
                    Directory.Delete(_tempPath, true);
            }
            catch { }
        }
    }
}
