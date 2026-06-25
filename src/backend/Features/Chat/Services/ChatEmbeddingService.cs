using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AcaAspireAiTemplate.Backend.Features.Chat;

internal sealed class ChatEmbeddingService(
    ChatOptions options,
    IManagedIdentityTokenProvider tokenProvider,
    IHttpClientFactory httpClientFactory) : IChatEmbeddingService
{
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        return options.IsLocalMode
            ? await GenerateLocalEmbeddingAsync(text, cancellationToken)
            : await GenerateAzureEmbeddingAsync(text, cancellationToken);
    }

    private async Task<float[]> GenerateAzureEmbeddingAsync(string text, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient();
        var usingManagedIdentity = await ConfigureOpenAiAuthAsync(client, cancellationToken);
        var requestUri = BuildEmbeddingsRequestUri(options.AzureOpenAi!.Endpoint, options.EmbeddingModelId);
        using var payload = new StringContent(
            JsonSerializer.Serialize(new { input = text, dimensions = options.EmbeddingDimensions }),
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync(requestUri, payload, cancellationToken);

        if (IsAuthFailure(response.StatusCode)
            && usingManagedIdentity
            && !string.IsNullOrWhiteSpace(options.AzureOpenAi!.ApiKey))
        {
            using var retryClient = httpClientFactory.CreateClient();
            retryClient.DefaultRequestHeaders.Add("api-key", options.AzureOpenAi.ApiKey);
            using var retryPayload = new StringContent(
                JsonSerializer.Serialize(new { input = text, dimensions = options.EmbeddingDimensions }),
                Encoding.UTF8,
                "application/json");
            using var retryResponse = await retryClient.PostAsync(requestUri, retryPayload, cancellationToken);
            retryResponse.EnsureSuccessStatusCode();
            return await ParseEmbeddingAsync(retryResponse, cancellationToken);
        }

        response.EnsureSuccessStatusCode();
        return await ParseEmbeddingAsync(response, cancellationToken);
    }

    private async Task<float[]> GenerateLocalEmbeddingAsync(string text, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient();
        using var payload = new StringContent(
            JsonSerializer.Serialize(new { model = options.EmbeddingModelId, input = text }),
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync($"{options.LocalLlmEmbedBaseUrl.TrimEnd('/')}/v1/embeddings", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await ParseEmbeddingAsync(response, cancellationToken);
    }

    private async Task<bool> ConfigureOpenAiAuthAsync(HttpClient client, CancellationToken cancellationToken)
    {
        if (string.Equals(options.OpenAiAuthMode, "managed-identity", StringComparison.OrdinalIgnoreCase))
        {
            var token = await tokenProvider.TryAcquireTokenAsync(
                "https://cognitiveservices.azure.com",
                options.ManagedIdentityClientId,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(options.AzureOpenAi?.ApiKey))
        {
            throw new InvalidOperationException(
                "OpenAI/Foundry authentication is unavailable. Set AZURE_OPENAI_API_KEY or provide managed identity runtime.");
        }

        client.DefaultRequestHeaders.Add("api-key", options.AzureOpenAi.ApiKey);
        return false;
    }

    private static bool IsAuthFailure(System.Net.HttpStatusCode statusCode) =>
        statusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden;

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
            .Select(x => x.GetSingle())
            .ToArray();
    }
}
