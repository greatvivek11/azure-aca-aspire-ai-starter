using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
namespace AIHub.Backend.Infrastructure.Ai;

public sealed class FoundryChatService : IAiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AzureOpenAiRuntimeSettings _settings;
    private readonly string _authMode;
    private readonly string? _managedIdentityClientId;

    public FoundryChatService(
        IHttpClientFactory httpClientFactory,
        AzureOpenAiRuntimeSettings settings,
        string authMode,
        string? managedIdentityClientId)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _authMode = authMode;
        _managedIdentityClientId = managedIdentityClientId;
    }

    public async Task<string> InvokePromptAsync(string prompt)
    {
        var requestUri = BuildChatCompletionsRequestUri(_settings.Endpoint, _settings.ModelId);
        var requestBody = new
        {
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = prompt
                }
            },
            temperature = 0.2
        };

        using var payload = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");
        using var client = _httpClientFactory.CreateClient();
        var usingManagedIdentity = await ConfigureFoundryAuthAsync(client, _settings.ApiKey, _authMode, _managedIdentityClientId);
        using var response = await client.PostAsync(requestUri, payload);

        if (IsAuthFailure(response.StatusCode) && usingManagedIdentity && !string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            using var retryClient = _httpClientFactory.CreateClient();
            retryClient.DefaultRequestHeaders.Add("api-key", _settings.ApiKey);
            using var retryPayload = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");
            using var retryResponse = await retryClient.PostAsync(requestUri, retryPayload);
            retryResponse.EnsureSuccessStatusCode();

            var retryJson = await retryResponse.Content.ReadAsStringAsync();
            using var retryDocument = JsonDocument.Parse(retryJson);
            var retryContent = retryDocument.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return retryContent?.Trim() ?? string.Empty;
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return content?.Trim() ?? string.Empty;
    }

    private static bool IsAuthFailure(System.Net.HttpStatusCode statusCode) =>
        statusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden;

    private static async Task<bool> ConfigureFoundryAuthAsync(
        HttpClient client,
        string? apiKey,
        string authMode,
        string? managedIdentityClientId)
    {
        if (string.Equals(authMode, "managed-identity", StringComparison.OrdinalIgnoreCase))
        {
            var token = await TryAcquireFoundryTokenAsync(managedIdentityClientId, client);
            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Foundry/OpenAI authentication failed. Provide AZURE_OPENAI_API_KEY or enable managed identity runtime.");
        }

        client.DefaultRequestHeaders.Add("api-key", apiKey);
        return false;
    }

    private static async Task<string?> TryAcquireFoundryTokenAsync(string? managedIdentityClientId, HttpClient client)
    {
        try
        {
            return await AcquireManagedIdentityTokenAsync(
                "https://cognitiveservices.azure.com",
                managedIdentityClientId,
                client);
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

    private static async Task<string> AcquireManagedIdentityTokenAsync(
        string resource,
        string? managedIdentityClientId,
        HttpClient client)
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

    private static string BuildChatCompletionsRequestUri(Uri endpoint, string deployment)
    {
        var baseUri = NormalizeOpenAiEndpointBase(endpoint);
        return $"{baseUri}/openai/deployments/{deployment}/chat/completions?api-version=2024-10-21";
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
}
