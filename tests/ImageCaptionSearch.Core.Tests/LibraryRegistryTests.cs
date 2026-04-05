using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageCaptionSearch.Core.Interfaces;
using ImageCaptionSearch.Core.Services;
using Moq;
using Xunit;

namespace ImageCaptionSearch.Core.Tests;

public class LibraryRegistryTests : IDisposable
{
    private readonly string _tempPath;
    private readonly Mock<ILibraryService> _mockLibraryService;
    private readonly LibraryRegistryService _registry;

    public LibraryRegistryTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "BlowfishTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempPath);
        _mockLibraryService = new Mock<ILibraryService>();
        _registry = new LibraryRegistryService(_mockLibraryService.Object, _tempPath);
    }

    [Fact]
    public async Task AddLibraryAsync_ShouldCreateLibrary_WhenPathIsValid()
    {
        // Arrange
        var libPath = Path.Combine(_tempPath, "Lib1");
        Directory.CreateDirectory(libPath);

        // Act
        var lib = await _registry.AddLibraryAsync(libPath, "My Library", CancellationToken.None);

        // Assert
        Assert.NotNull(lib);
        Assert.Equal("My Library", lib.DisplayName);
        Assert.Equal(libPath, lib.RootPath);
        _mockLibraryService.Verify(s => s.InitializeLibraryAsync(lib, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddLibraryAsync_ShouldThrow_WhenPathDoesNotExist()
    {
        // Arrange
        var libPath = Path.Combine(_tempPath, "NonExistent");

        // Act & Assert
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => 
            _registry.AddLibraryAsync(libPath, "Fail", CancellationToken.None));
    }

    [Fact]
    public async Task AddLibraryAsync_ShouldThrow_WhenLibrariesAreNested()
    {
        // Arrange
        var parentPath = Path.Combine(_tempPath, "Parent");
        var childPath = Path.Combine(parentPath, "Child");
        Directory.CreateDirectory(parentPath);
        Directory.CreateDirectory(childPath);

        await _registry.AddLibraryAsync(parentPath, "Parent Lib", CancellationToken.None);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            _registry.AddLibraryAsync(childPath, "Child Lib", CancellationToken.None));
    }

    public void Dispose()
    {
        try 
        {
            if (Directory.Exists(_tempPath))
            {
                Directory.Delete(_tempPath, true);
            }
        }
        catch 
        {
            // Ignore cleanup errors in tests
        }
    }
}
