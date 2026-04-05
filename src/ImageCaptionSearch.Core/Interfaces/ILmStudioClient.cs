namespace ImageCaptionSearch.Core.Interfaces;

public record LmModelItem(string Id, string Object, long Created, string OwnedBy);

public interface ILmStudioClient
{
    Task<bool> TestConnectionAsync(string baseUrl, CancellationToken ct = default);
    Task<IReadOnlyList<LmModelItem>> GetModelsAsync(string baseUrl, CancellationToken ct = default);
    Task<string> GetChatCompletionAsync(string baseUrl, string modelId, string prompt, byte[]? imageBytes = null, CancellationToken ct = default);
    Task<float[]> GetEmbeddingAsync(string baseUrl, string modelId, string text, CancellationToken ct = default);
}
