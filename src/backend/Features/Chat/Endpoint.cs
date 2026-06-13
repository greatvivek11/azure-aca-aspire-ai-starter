using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIHub.Backend.Infrastructure.Ai;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AIHub.Backend.Features.Chat;

public static class Endpoint
{
    public static void MapChatEndpoint(this IEndpointRouteBuilder app, ChatOptions options)
    {
        app.MapPost("/v1/chat", async (ChatRequest request, IAiService aiService, IHttpClientFactory httpClientFactory) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return Results.BadRequest("message is required.");
            }

            var mode = (request.Mode ?? "general").Trim().ToLowerInvariant();
            if (mode is "docs" or "rag")
            {
                if (!options.SearchConfigured)
                {
                    return Results.BadRequest("RAG mode is not configured. Missing search endpoint/index or usable search authentication.");
                }

                var embedding = await GenerateEmbeddingAsync(
                    request.Message,
                    options.AzureOpenAi.Endpoint,
                    options.AzureOpenAi.ApiKey,
                    options.OpenAiAuthMode,
                    options.EmbeddingModelId,
                    options.EmbeddingDimensions,
                    options.ManagedIdentityClientId,
                    httpClientFactory);

                var searchResults = await SearchRelevantChunksWithFallbackAsync(
                    embedding,
                    request.DocumentId,
                    options,
                    httpClientFactory);

                var citations = new List<ChatCitation>();
                var contextParts = new List<string>();

                foreach (var result in searchResults)
                {
                    if (string.IsNullOrWhiteSpace(result.Content))
                    {
                        continue;
                    }

                    contextParts.Add(result.Content);
                    citations.Add(new ChatCitation(
                        result.DocumentId,
                        result.ChunkId,
                        result.FileName));
                }

                if (contextParts.Count == 0)
                {
                    return Results.Ok(new ChatResponse(
                        "I could not find relevant indexed content for this question yet. Upload and ingest a file first.",
                        citations));
                }

                var prompt = BuildRagPrompt(request.Message, contextParts);
                var answer = await aiService.InvokePromptAsync(prompt);
                return Results.Ok(new ChatResponse(answer, citations));
            }

            var generalAnswer = await aiService.InvokePromptAsync(request.Message);
            return Results.Ok(new ChatResponse(generalAnswer, []));
        });
    }

    private static string BuildRagPrompt(string question, IEnumerable<string> contexts)
    {
        var contextBlock = string.Join("\n\n---\n\n", contexts);
        return $"""
You are an enterprise copilot assistant.
Answer only from the provided context. If context is insufficient, say so.

Context:
{contextBlock}

Question:
{question}
""";
    }

    private static async Task<float[]> GenerateEmbeddingAsync(
        string text,
        Uri endpoint,
        string? apiKey,
        string openAiAuthMode,
        string embeddingDeployment,
        int embeddingDimensions,
        string? managedIdentityClientId,
        IHttpClientFactory httpClientFactory)
    {
        using var client = httpClientFactory.CreateClient();
        var usingManagedIdentity = await ConfigureOpenAiAuthAsync(client, apiKey, openAiAuthMode, managedIdentityClientId);
        var requestUri = BuildEmbeddingsRequestUri(endpoint, embeddingDeployment);
        using var payload = new StringContent(
            JsonSerializer.Serialize(new { input = text, dimensions = embeddingDimensions }),
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync(requestUri, payload);
        if (IsAuthFailure(response.StatusCode) && usingManagedIdentity && !string.IsNullOrWhiteSpace(apiKey))
        {
            using var retryClient = httpClientFactory.CreateClient();
            retryClient.DefaultRequestHeaders.Add("api-key", apiKey);
            using var retryPayload = new StringContent(
                JsonSerializer.Serialize(new { input = text, dimensions = embeddingDimensions }),
                Encoding.UTF8,
                "application/json");
            using var retryResponse = await retryClient.PostAsync(requestUri, retryPayload);
            retryResponse.EnsureSuccessStatusCode();
            return await ParseEmbeddingAsync(retryResponse);
        }

        response.EnsureSuccessStatusCode();
        return await ParseEmbeddingAsync(response);
    }

    private static bool IsAuthFailure(System.Net.HttpStatusCode statusCode) =>
        statusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden;

    private static async Task<float[]> ParseEmbeddingAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement
            .GetProperty("data")[0]
            .GetProperty("embedding")
            .EnumerateArray()
            .Select(x => x.GetSingle())
            .ToArray();
    }

    private static async Task<bool> ConfigureOpenAiAuthAsync(
        HttpClient client,
        string? apiKey,
        string openAiAuthMode,
        string? managedIdentityClientId)
    {
        if (string.Equals(openAiAuthMode, "managed-identity", StringComparison.OrdinalIgnoreCase))
        {
            var token = await TryAcquireFoundryTokenAsync(managedIdentityClientId, client, "https://cognitiveservices.azure.com");
            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OpenAI/Foundry authentication is unavailable. Set AZURE_OPENAI_API_KEY or provide managed identity runtime.");
        }

        client.DefaultRequestHeaders.Add("api-key", apiKey);
        return false;
    }

    private static async Task<string?> TryAcquireFoundryTokenAsync(
        string? managedIdentityClientId,
        HttpClient client,
        string resource)
    {
        try
        {
            return await AcquireManagedIdentityTokenAsync(resource, managedIdentityClientId, client);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
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

    private static async Task<List<SearchChunkDocument>> SearchRelevantChunksWithFallbackAsync(
        float[] embedding,
        Guid? documentId,
        ChatOptions options,
        IHttpClientFactory httpClientFactory)
    {
        if (options.ManagedIdentityRuntimeAvailable)
        {
            try
            {
                var token = await AcquireManagedIdentityTokenAsync(options.ManagedIdentityClientId, httpClientFactory);
                return await SearchRelevantChunksAsync(embedding, documentId, options, httpClientFactory, bearerToken: token);
            }
            catch (HttpRequestException ex) when (!string.IsNullOrWhiteSpace(options.SearchApiKey) && ShouldFallbackToApiKey(ex))
            {
                return await SearchRelevantChunksAsync(embedding, documentId, options, httpClientFactory);
            }
            catch (InvalidOperationException ex) when (!string.IsNullOrWhiteSpace(options.SearchApiKey) && IsManagedIdentityConfigurationIssue(ex))
            {
                return await SearchRelevantChunksAsync(embedding, documentId, options, httpClientFactory);
            }
        }

        return await SearchRelevantChunksAsync(embedding, documentId, options, httpClientFactory);
    }

    private static async Task<List<SearchChunkDocument>> SearchRelevantChunksAsync(
        float[] embedding,
        Guid? documentId,
        ChatOptions options,
        IHttpClientFactory httpClientFactory,
        string? bearerToken = null)
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
            ["top"] = 5,
            ["select"] = "id,documentId,chunkId,fileName,content",
            ["vectorQueries"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["kind"] = "vector",
                    ["fields"] = "contentVector",
                    ["k"] = 5,
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
        using var response = await client.PostAsync(requestUri, payload);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
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

    private static async Task<string> AcquireManagedIdentityTokenAsync(string? managedIdentityClientId, IHttpClientFactory httpClientFactory)
    {
        using var client = httpClientFactory.CreateClient();
        return await AcquireManagedIdentityTokenAsync("https://search.azure.com", managedIdentityClientId, client);
    }

    private static async Task<string> AcquireManagedIdentityTokenAsync(string resource, string? managedIdentityClientId, HttpClient client)
    {
        var identityEndpoint = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT");
        var identityHeader = Environment.GetEnvironmentVariable("IDENTITY_HEADER");
        if (string.IsNullOrWhiteSpace(identityEndpoint) || string.IsNullOrWhiteSpace(identityHeader))
        {
            throw new InvalidOperationException("Managed identity endpoint is not available in this environment.");
        }

        var requestUri = $"{identityEndpoint}?api-version=2019-08-01&resource={Uri.EscapeDataString(resource)}";
        if (!string.IsNullOrWhiteSpace(managedIdentityClientId))
        {
            requestUri = $"{requestUri}&client_id={Uri.EscapeDataString(managedIdentityClientId)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Add("X-IDENTITY-HEADER", identityHeader);

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("access_token", out var tokenElement))
        {
            throw new InvalidOperationException("Managed identity token response did not include access_token.");
        }

        return tokenElement.GetString()
               ?? throw new InvalidOperationException("Managed identity access_token was empty.");
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

public sealed record ChatOptions(
    AzureOpenAiRuntimeSettings AzureOpenAi,
    string OpenAiAuthMode,
    string EmbeddingModelId,
    int EmbeddingDimensions,
    string SearchEndpoint,
    string SearchIndexName,
    string? SearchApiKey,
    string? ManagedIdentityClientId,
    bool UseManagedIdentity)
{
    public bool ManagedIdentityRuntimeAvailable =>
        UseManagedIdentity &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT")) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IDENTITY_HEADER"));

    public bool SearchConfigured =>
        !string.IsNullOrWhiteSpace(SearchEndpoint) &&
        !string.IsNullOrWhiteSpace(SearchIndexName) &&
        (ManagedIdentityRuntimeAvailable || !string.IsNullOrWhiteSpace(SearchApiKey));
}

internal sealed record ChatRequest(string Message, string? Mode, Guid? DocumentId);
internal sealed record ChatCitation(string DocumentId, string ChunkId, string FileName);
internal sealed record ChatResponse(string Answer, IReadOnlyList<ChatCitation> Citations);

internal sealed class SearchChunkDocument
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string ChunkId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
