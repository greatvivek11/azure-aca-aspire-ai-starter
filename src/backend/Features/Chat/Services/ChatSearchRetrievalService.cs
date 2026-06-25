using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AcaAspireAiTemplate.Backend.Features.Chat;

internal sealed class ChatSearchRetrievalService(
    ChatOptions options,
    IManagedIdentityTokenProvider tokenProvider,
    IHttpClientFactory httpClientFactory) : IChatSearchRetrievalService
{
    public async Task<List<SearchChunkDocument>> SearchRelevantChunksAsync(
        float[] embedding,
        Guid? documentId,
        CancellationToken cancellationToken = default)
    {
        return options.IsLocalMode
            ? await SearchRelevantChunksFromQdrantAsync(embedding, documentId, cancellationToken)
            : await SearchRelevantChunksWithFallbackAsync(embedding, documentId, cancellationToken);
    }

    private async Task<List<SearchChunkDocument>> SearchRelevantChunksWithFallbackAsync(
        float[] embedding,
        Guid? documentId,
        CancellationToken cancellationToken)
    {
        if (options.ManagedIdentityRuntimeAvailable)
        {
            try
            {
                var token = await tokenProvider.AcquireTokenAsync(
                    "https://search.azure.com",
                    options.ManagedIdentityClientId,
                    cancellationToken);

                return await SearchRelevantChunksInAzureAsync(
                    embedding,
                    documentId,
                    bearerToken: token,
                    cancellationToken);
            }
            catch (HttpRequestException ex) when (!string.IsNullOrWhiteSpace(options.SearchApiKey) && ShouldFallbackToApiKey(ex))
            {
                return await SearchRelevantChunksInAzureAsync(embedding, documentId, null, cancellationToken);
            }
            catch (InvalidOperationException ex) when (!string.IsNullOrWhiteSpace(options.SearchApiKey) && IsManagedIdentityConfigurationIssue(ex))
            {
                return await SearchRelevantChunksInAzureAsync(embedding, documentId, null, cancellationToken);
            }
        }

        return await SearchRelevantChunksInAzureAsync(embedding, documentId, null, cancellationToken);
    }

    private async Task<List<SearchChunkDocument>> SearchRelevantChunksFromQdrantAsync(
        float[] embedding,
        Guid? documentId,
        CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient();
        var searchPayload = new Dictionary<string, object?>
        {
            ["vector"] = embedding,
            ["limit"] = options.LocalRagTopK,
            ["with_payload"] = true
        };

        if (documentId.HasValue)
        {
            searchPayload["filter"] = new
            {
                must = new[]
                {
                    new
                    {
                        key = "documentId",
                        match = new { value = documentId.Value.ToString() }
                    }
                }
            };
        }

        using var payload = new StringContent(
            JsonSerializer.Serialize(searchPayload),
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync(
            $"{options.QdrantUrl.TrimEnd('/')}/collections/{options.QdrantCollection}/points/search",
            payload,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var chunks = new List<SearchChunkDocument>();
        if (!document.RootElement.TryGetProperty("result", out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return chunks;
        }

        foreach (var element in values.EnumerateArray())
        {
            if (!element.TryGetProperty("payload", out var payloadElement))
            {
                continue;
            }

            chunks.Add(new SearchChunkDocument
            {
                Id = element.TryGetProperty("id", out var idElement) ? idElement.ToString() : string.Empty,
                DocumentId = payloadElement.TryGetProperty("documentId", out var documentIdElement) ? documentIdElement.GetString() ?? string.Empty : string.Empty,
                ChunkId = payloadElement.TryGetProperty("chunkId", out var chunkIdElement) ? chunkIdElement.GetString() ?? string.Empty : string.Empty,
                FileName = payloadElement.TryGetProperty("fileName", out var fileNameElement) ? fileNameElement.GetString() ?? string.Empty : string.Empty,
                Content = payloadElement.TryGetProperty("content", out var contentElement) ? contentElement.GetString() ?? string.Empty : string.Empty
            });
        }

        return chunks;
    }

    private async Task<List<SearchChunkDocument>> SearchRelevantChunksInAzureAsync(
        float[] embedding,
        Guid? documentId,
        string? bearerToken,
        CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient();
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(options.SearchApiKey))
            {
                throw new InvalidOperationException("AZURE_SEARCH_API_KEY is required when managed identity is unavailable.");
            }

            client.DefaultRequestHeaders.Add("api-key", options.SearchApiKey);
        }

        var requestUri = $"{options.SearchEndpoint.TrimEnd('/')}/indexes/{options.SearchIndexName}/docs/search?api-version=2024-07-01";
        var requestBody = new Dictionary<string, object?>
        {
            ["search"] = "*",
            ["top"] = options.LocalRagTopK,
            ["select"] = "id,documentId,chunkId,fileName,content",
            ["vectorQueries"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["kind"] = "vector",
                    ["fields"] = "contentVector",
                    ["k"] = options.LocalRagTopK,
                    ["vector"] = embedding
                }
            }
        };

        if (documentId.HasValue)
        {
            var escapedDocumentId = documentId.Value.ToString().Replace("'", "''");
            requestBody["filter"] = $"documentId eq '{escapedDocumentId}'";
        }

        using var payload = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync(requestUri, payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var chunks = new List<SearchChunkDocument>();
        if (!document.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return chunks;
        }

        foreach (var element in values.EnumerateArray())
        {
            chunks.Add(new SearchChunkDocument
            {
                Id = element.GetProperty("id").GetString() ?? string.Empty,
                DocumentId = element.GetProperty("documentId").GetString() ?? string.Empty,
                ChunkId = element.GetProperty("chunkId").GetString() ?? string.Empty,
                FileName = element.GetProperty("fileName").GetString() ?? string.Empty,
                Content = element.GetProperty("content").GetString() ?? string.Empty
            });
        }

        return chunks;
    }

    private static bool ShouldFallbackToApiKey(HttpRequestException ex)
    {
        if (!ex.StatusCode.HasValue)
        {
            return true;
        }

        return ex.StatusCode.Value is
            System.Net.HttpStatusCode.BadRequest or
            System.Net.HttpStatusCode.Unauthorized or
            System.Net.HttpStatusCode.Forbidden or
            System.Net.HttpStatusCode.NotFound or
            System.Net.HttpStatusCode.RequestTimeout or
            System.Net.HttpStatusCode.TooManyRequests or
            System.Net.HttpStatusCode.BadGateway or
            System.Net.HttpStatusCode.ServiceUnavailable or
            System.Net.HttpStatusCode.GatewayTimeout;
    }

    private static bool IsManagedIdentityConfigurationIssue(InvalidOperationException ex)
    {
        var message = ex.Message;
        return message.Contains("Managed identity endpoint is not available", StringComparison.OrdinalIgnoreCase)
            || message.Contains("access_token", StringComparison.OrdinalIgnoreCase);
    }
}
