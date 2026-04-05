using ImageCaptionSearch.Core.Models;

namespace ImageCaptionSearch.Core.Interfaces;

public interface ISearchService
{
    Task<IReadOnlyList<SearchResultItem>> SearchAsync(Guid libraryId, SearchQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<SearchResultItem>> FindSimilarImagesAsync(Guid libraryId, string imageId, int limit = 10, CancellationToken ct = default);
    Task<IReadOnlyList<SearchResultItem>> FindSimilarFacesAsync(Guid libraryId, string faceId, int limit = 10, CancellationToken ct = default);
}
