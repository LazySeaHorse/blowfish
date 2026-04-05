using System;
using System.Threading;
using System.Threading.Tasks;
using ImageCaptionSearch.Core.Interfaces;
using ImageCaptionSearch.Core.Models;

namespace ImageCaptionSearch.Core.Services;

public class EmbeddingService : IEmbeddingService
{
    private readonly ILmStudioClient _client;

    public EmbeddingService(ILmStudioClient client)
    {
        _client = client;
    }

    public async Task<EmbeddingRecord> GenerateEmbeddingAsync(string parentId, string text, AppSettings settings, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(settings.EmbeddingModelId)) 
            throw new InvalidOperationException("Embedding model not selected in settings.");

        var vector = await _client.GetEmbeddingAsync(
            settings.LmStudioBaseUrl, 
            settings.EmbeddingModelId, 
            text, 
            ct);

        // Precompute norm for fast cosine similarity: sum of squares
        double sumSquares = 0;
        foreach (var v in vector) sumSquares += v * v;
        var norm = Math.Sqrt(sumSquares);

        return new EmbeddingRecord(
            parentId,
            settings.EmbeddingModelId,
            vector.Length,
            vector,
            norm,
            DateTime.UtcNow
        );
    }
}
