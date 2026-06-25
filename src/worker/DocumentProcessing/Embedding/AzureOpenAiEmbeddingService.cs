using System.Text;
using System.Text.Json;

internal sealed class AzureOpenAiEmbeddingService(
    WorkerRuntimeOptions runtimeOptions,
    AzureAuthenticator authenticator,
    IHttpClientFactory httpClientFactory) : IEmbeddingService
{
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        const int MaxRetries = 5;
        var requestUri = BuildEmbeddingsRequestUri(runtimeOptions.GetOpenAiEndpointOrFallback(), runtimeOptions.EmbeddingModelId);

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            using var client = httpClientFactory.CreateClient();
            var usingManagedIdentity = await authenticator.ConfigureOpenAiAuthAsync(
                client,
                runtimeOptions.OpenAiApiKey,
                runtimeOptions.OpenAiAuthMode,
                runtimeOptions.ManagedIdentityClientId);

            using var payload = new StringContent(
                JsonSerializer.Serialize(new { input = text, dimensions = runtimeOptions.EmbeddingDimensions }),
                Encoding.UTF8,
                "application/json");
            using var response = await client.PostAsync(requestUri, payload, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < MaxRetries)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta
                    ?? TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempt + 1)));
                await Task.Delay(retryAfter, cancellationToken);
                continue;
            }

            if (AzureAuthenticator.IsAuthFailure(response.StatusCode)
                && usingManagedIdentity
                && !string.IsNullOrWhiteSpace(runtimeOptions.OpenAiApiKey))
            {
                using var retryClient = httpClientFactory.CreateClient();
                retryClient.DefaultRequestHeaders.Add("api-key", runtimeOptions.OpenAiApiKey);
                using var retryPayload = new StringContent(
                    JsonSerializer.Serialize(new { input = text, dimensions = runtimeOptions.EmbeddingDimensions }),
                    Encoding.UTF8,
                    "application/json");
                using var retryResponse = await retryClient.PostAsync(requestUri, retryPayload, cancellationToken);
                retryResponse.EnsureSuccessStatusCode();
                return await ParseEmbeddingAsync(retryResponse, cancellationToken);
            }

            response.EnsureSuccessStatusCode();
            return await ParseEmbeddingAsync(response, cancellationToken);
        }

        throw new HttpRequestException("Embedding request failed after exhausting retries due to rate limiting (429 Too Many Requests).", null, System.Net.HttpStatusCode.TooManyRequests);
    }

    private static string BuildEmbeddingsRequestUri(Uri endpoint, string embeddingDeployment)
    {
        var baseUri = NormalizeOpenAiEndpointBase(endpoint);
        return $"{baseUri}/openai/deployments/{embeddingDeployment}/embeddings?api-version=2024-10-21";
    }

    private static string NormalizeOpenAiEndpointBase(Uri endpoint)
    {
        var authority = endpoint.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        var normalizedPath = endpoint.AbsolutePath.TrimEnd('/');

        if (normalizedPath.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath[..^"/openai/v1".Length];
        }
        else if (normalizedPath.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath[..^"/openai".Length];
        }

        normalizedPath = normalizedPath.TrimEnd('/');
        return string.IsNullOrWhiteSpace(normalizedPath) ? authority : $"{authority}{normalizedPath}";
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
