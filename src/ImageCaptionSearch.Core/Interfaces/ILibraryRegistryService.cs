using ImageCaptionSearch.Core.Models;

namespace ImageCaptionSearch.Core.Interfaces;

public interface ILibraryRegistryService
{
    Task<IReadOnlyList<Library>> GetLibrariesAsync(CancellationToken ct = default);
    Task<Library?> GetLibraryByIdAsync(Guid id, CancellationToken ct = default);
    Task<Library?> GetLastOpenedLibraryAsync(CancellationToken ct = default);
    Task SetLastOpenedLibraryAsync(Guid id, CancellationToken ct = default);
    Task<Library> AddLibraryAsync(string rootPath, string displayName, CancellationToken ct = default);
    Task RemoveLibraryAsync(Guid id, bool deleteLocalData = false, CancellationToken ct = default);
    Task UpdateLibraryAsync(Library library, CancellationToken ct = default);
}
