using System.Text;
using System.Text.Json;

internal sealed class LlamaCppEmbeddingService(
    WorkerRuntimeOptions runtimeOptions,
    IHttpClientFactory httpClientFactory) : IEmbeddingService
{
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        using var localClient = httpClientFactory.CreateClient();
        using var localPayload = new StringContent(
            JsonSerializer.Serialize(new { model = runtimeOptions.EmbeddingModelId, input = text }),
            Encoding.UTF8,
            "application/json");

        using var localResponse = await localClient.PostAsync(
            $"{runtimeOptions.LocalLlmBaseUrl.TrimEnd('/')}/v1/embeddings",
            localPayload,
            cancellationToken);

        localResponse.EnsureSuccessStatusCode();
        return await ParseEmbeddingAsync(localResponse, cancellationToken);
    }

    private static async Task<float[]> ParseEmbeddingAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        return document.RootElement
            .GetProperty("data")[0]
            .GetProperty("embedding")
            .EnumerateArray()
            .Select(element => element.GetSingle())
            .ToArray();
    }
}
