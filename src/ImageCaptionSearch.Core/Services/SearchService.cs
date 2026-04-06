using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ImageCaptionSearch.Core.Interfaces;
using ImageCaptionSearch.Core.Models;
using System.Globalization;

namespace ImageCaptionSearch.Core.Services;

public class SearchService : ISearchService
{
    private readonly ILibraryRegistryService _libraryRegistry;
    private readonly ILibraryService _libraryService;
    private readonly IEmbeddingService _embeddingService;
    private readonly ISettingsService _settingsService;

    public SearchService(
        ILibraryRegistryService libraryRegistry,
        ILibraryService libraryService,
        IEmbeddingService embeddingService,
        ISettingsService settingsService)
    {
        _libraryRegistry = libraryRegistry;
        _libraryService = libraryService;
        _embeddingService = embeddingService;
        _settingsService = settingsService;
    }

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(Guid libraryId, SearchQuery query, CancellationToken ct = default)
    {
        var library = await _libraryRegistry.GetLibraryByIdAsync(libraryId, ct);
        if (library == null) throw new InvalidOperationException("Library not found.");

        if (query.Mode == SearchMode.Caption)
        {
            return await ExecuteCaptionSearchAsync(library, query, ct);
        }
        else
        {
            return await ExecuteSemanticSearchAsync(library, query, ct);
        }
    }

    private async Task<IReadOnlyList<SearchResultItem>> ExecuteCaptionSearchAsync(Library library, SearchQuery query, CancellationToken ct)
    {
        var catalogPath = Path.Combine(library.RootPath, ".imagecaptionsearch", "catalog.db");
        using var connection = new SqliteConnection($"Data Source={catalogPath}");
        await connection.OpenAsync(ct);

        using var command = connection.CreateCommand();
        
        var whereClause = "WHERE i.is_missing = 0";
        if (query.FilterHasHuman.HasValue)
        {
            whereClause += " AND c.has_human = @human";
            command.Parameters.AddWithValue("@human", query.FilterHasHuman.Value ? 1 : 0);
        }

        if (string.IsNullOrWhiteSpace(query.QueryText))
        {
            command.CommandText = $@"
                SELECT i.id, i.relative_path, i.file_name, c.caption, c.has_human, i.thumbnail_rel_path, 1.0 as score, i.last_error
                FROM images i
                INNER JOIN captions c ON i.id = c.image_id
                {whereClause}
                ORDER BY i.discovered_utc DESC
                LIMIT @limit OFFSET @offset";
        }
        else
        {
            // Note: score for bm25 is negative (smaller is better), but for consistency we use absolute or inverse.
            // Actually, we'll just return it and UI can deal with it or we just sort by it.
            command.CommandText = $@"
                SELECT i.id, i.relative_path, i.file_name, c.caption, c.has_human, i.thumbnail_rel_path, bm25(captions_fts) as score, i.last_error
                FROM images i
                INNER JOIN captions c ON i.id = c.image_id
                JOIN captions_fts ON c.image_id = captions_fts.image_id
                {whereClause} AND captions_fts MATCH @query
                ORDER BY score
                LIMIT @limit OFFSET @offset";
            command.Parameters.AddWithValue("@query", query.QueryText.Trim());
        }

        command.Parameters.AddWithValue("@limit", query.Limit);
        command.Parameters.AddWithValue("@offset", query.Offset);

        var results = new List<SearchResultItem>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new SearchResultItem(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4) != 0,
                Math.Abs(reader.GetDouble(6)), 
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(7) ? null : reader.GetString(7)
            ));
        }
        return results;
    }

    private async Task<IReadOnlyList<SearchResultItem>> ExecuteSemanticSearchAsync(Library library, SearchQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.QueryText))
        {
            return await ExecuteCaptionSearchAsync(library, query with { Mode = SearchMode.Caption }, ct);
        }

        // 1. Get query embedding
        var settings = await _settingsService.GetSettingsAsync(ct);
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync("query", query.QueryText, settings, true, ct);

        // 2. Load all embeddings for this model
        var embeddings = await _libraryService.GetEmbeddingsAsync(library, queryEmbedding.ModelName, ct);
        if (!embeddings.Any()) return Array.Empty<SearchResultItem>();

        // 3. Compute similarities in memory
        var scored = embeddings.Select(e => new 
        { 
            ImageId = e.ParentId, 
            Score = ComputeCosineSimilarity(queryEmbedding.Vector, queryEmbedding.VectorNorm, e.Vector, e.VectorNorm) 
        })
        .OrderByDescending(x => x.Score)
        .Skip(query.Offset)
        .Take(query.Limit)
        .ToList();

        if (!scored.Any()) return Array.Empty<SearchResultItem>();

        // 4. Fetch metadata for top items
        var imageIds = scored.Select(s => s.ImageId).ToList();
        var metadata = await FetchMetadataBatchAsync(library, imageIds, ct);

        // 5. Merge and return
        var final = new List<SearchResultItem>();
        foreach (var s in scored)
        {
            if (metadata.TryGetValue(s.ImageId, out var m))
            {
                if (query.FilterHasHuman.HasValue && m.HasHuman != query.FilterHasHuman.Value) continue;
                final.Add(m with { Score = s.Score });
            }
        }
        return final;
    }

    private static double ComputeCosineSimilarity(float[] v1, double n1, float[] v2, double n2)
    {
        if (n1 == 0 || n2 == 0) return 0;
        double dotProduct = 0;
        for (int i = 0; i < v1.Length; i++)
        {
            dotProduct += v1[i] * v2[i];
        }
        return dotProduct / (n1 * n2);
    }

    private async Task<Dictionary<string, SearchResultItem>> FetchMetadataBatchAsync(Library library, List<string> ids, CancellationToken ct)
    {
        var catalogPath = Path.Combine(library.RootPath, ".imagecaptionsearch", "catalog.db");
        using var connection = new SqliteConnection($"Data Source={catalogPath}");
        await connection.OpenAsync(ct);

        using var command = connection.CreateCommand();
        var idList = string.Join(",", ids.Select(id => $"'{id}'"));
        command.CommandText = $@"
            SELECT i.id, i.relative_path, i.file_name, c.caption, c.has_human, i.thumbnail_rel_path, i.last_error
            FROM images i
            LEFT JOIN captions c ON i.id = c.image_id
            WHERE i.id IN ({idList})";

        var results = new Dictionary<string, SearchResultItem>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetString(0);
            results[id] = new SearchResultItem(
                id,
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                !reader.IsDBNull(4) && reader.GetInt32(4) != 0,
                0,
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)
            );
        }
        return results;
    }

    public async Task<IReadOnlyList<SearchResultItem>> FindSimilarImagesAsync(Guid libraryId, string imageId, int limit = 10, CancellationToken ct = default)
    {
        var library = await _libraryRegistry.GetLibraryByIdAsync(libraryId, ct);
        if (library == null) throw new InvalidOperationException("Library not found.");

        var embeddings = await _libraryService.GetEmbeddingsAsync(library, null, ct);
        var queryImg = embeddings.FirstOrDefault(e => e.ParentId == imageId);
        if (queryImg == null) return Array.Empty<SearchResultItem>();

        var scored = embeddings
            .Where(e => e.ParentId != imageId && e.ModelName == queryImg.ModelName)
            .Select(e => new 
            { 
                ImageId = e.ParentId, 
                Score = ComputeCosineSimilarity(queryImg.Vector, queryImg.VectorNorm, e.Vector, e.VectorNorm) 
            })
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .ToList();

        if (!scored.Any()) return Array.Empty<SearchResultItem>();

        var metadata = await FetchMetadataBatchAsync(library, scored.Select(s => s.ImageId).ToList(), ct);
        return scored
            .Where(s => metadata.ContainsKey(s.ImageId))
            .Select(s => metadata[s.ImageId] with { Score = s.Score })
            .ToList();
    }

    public async Task<IReadOnlyList<SearchResultItem>> FindSimilarFacesAsync(Guid libraryId, string faceId, int limit = 10, CancellationToken ct = default)
    {
        var library = await _libraryRegistry.GetLibraryByIdAsync(libraryId, ct);
        if (library == null) throw new InvalidOperationException("Library not found.");

        // 1. Get query face embedding
        var faceEmbeddings = await _libraryService.GetFaceEmbeddingsAsync(library, null, ct);
        var queryFace = faceEmbeddings.FirstOrDefault(f => f.FaceId == faceId);
        if (queryFace == null) return Array.Empty<SearchResultItem>();

        // 2. Compute similarities to all faces in library
        var scoredFaces = faceEmbeddings
            .Where(f => f.FaceId != faceId && f.ModelName == queryFace.ModelName)
            .Select(f => new 
            { 
                FaceId = f.FaceId, 
                Score = ComputeCosineSimilarity(queryFace.Vector, queryFace.VectorNorm, f.Vector, f.VectorNorm) 
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        if (!scoredFaces.Any()) return Array.Empty<SearchResultItem>();

        // 3. Map face IDs to image IDs
        var faceToImage = await FetchFaceToImageMapAsync(library, scoredFaces.Select(s => s.FaceId).ToList(), ct);

        // 4. Take top unique images
        var results = new List<SearchResultItem>();
        var seenImages = new HashSet<string>();
        
        foreach (var sf in scoredFaces)
        {
            if (faceToImage.TryGetValue(sf.FaceId, out var imgId) && !seenImages.Contains(imgId))
            {
                seenImages.Add(imgId);
                var meta = await FetchMetadataBatchAsync(library, new List<string> { imgId }, ct);
                if (meta.TryGetValue(imgId, out var item))
                {
                    results.Add(item with { Score = sf.Score });
                }
                if (results.Count >= limit) break;
            }
        }

        return results;
    }

    private async Task<Dictionary<string, string>> FetchFaceToImageMapAsync(Library library, List<string> faceIds, CancellationToken ct)
    {
        var catalogPath = Path.Combine(library.RootPath, ".imagecaptionsearch", "catalog.db");
        using var connection = new SqliteConnection($"Data Source={catalogPath}");
        await connection.OpenAsync(ct);

        using var command = connection.CreateCommand();
        var idList = string.Join(",", faceIds.Select(id => $"'{id}'"));
        command.CommandText = $"SELECT id, image_id FROM faces WHERE id IN ({idList})";

        var map = new Dictionary<string, string>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            map[reader.GetString(0)] = reader.GetString(1);
        }
        return map;
    }
}
