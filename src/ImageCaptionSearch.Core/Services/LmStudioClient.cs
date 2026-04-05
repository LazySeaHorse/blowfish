using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImageCaptionSearch.Core.Interfaces;

namespace ImageCaptionSearch.Core.Services;

public class LmStudioClient : ILmStudioClient
{
    private readonly HttpClient _httpClient;

    public LmStudioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<bool> TestConnectionAsync(string baseUrl, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{baseUrl.TrimEnd('/')}/v1/models", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<LmModelItem>> GetModelsAsync(string baseUrl, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"{baseUrl.TrimEnd('/')}/v1/models", ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ModelsResponse>(ct);
        return (IReadOnlyList<LmModelItem>?)result?.Data ?? Array.Empty<LmModelItem>();
    }

    public async Task<string> GetChatCompletionAsync(string baseUrl, string modelId, string prompt, byte[]? imageBytes = null, CancellationToken ct = default)
    {
        var url = $"{baseUrl.TrimEnd('/')}/v1/chat/completions";
        
        var contentList = new List<object>
        {
            new { type = "text", text = prompt }
        };

        if (imageBytes != null)
        {
            contentList.Add(new 
            { 
                type = "image_url", 
                image_url = new { url = $"data:image/jpeg;base64,{Convert.ToBase64String(imageBytes)}" } 
            });
        }

        var messages = new[]
        {
            new { role = "user", content = contentList }
        };

        var requestBody = new
        {
            model = modelId,
            messages = messages,
            temperature = 0.0,
            response_format = new { type = "json_object" }
        };

        var response = await _httpClient.PostAsJsonAsync(url, requestBody, ct);
        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(ct);
        return result?.Choices?[0]?.Message?.Content ?? throw new InvalidOperationException("Empty response from LM Studio.");
    }

    public async Task<float[]> GetEmbeddingAsync(string baseUrl, string modelId, string text, CancellationToken ct = default)
    {
        var url = $"{baseUrl.TrimEnd('/')}/v1/embeddings";
        var requestBody = new { model = modelId, input = text };

        var response = await _httpClient.PostAsJsonAsync(url, requestBody, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(ct);
        if (result?.Data == null || result.Data.Count == 0) throw new InvalidOperationException("Empty embedding response from LM Studio.");
        return result.Data[0].Embedding ?? throw new InvalidOperationException("Embedding vector is null.");
    }

    private sealed class ModelsResponse { public List<LmModelItem>? Data { get; set; } }
    private sealed class ChatCompletionResponse { public List<Choice>? Choices { get; set; } }
    private sealed class Choice { public Message? Message { get; set; } }
    private sealed class Message { public string? Content { get; set; } }
    private sealed class EmbeddingResponse { public List<EmbeddingData>? Data { get; set; } }
    private sealed class EmbeddingData { public float[]? Embedding { get; set; } }
}
