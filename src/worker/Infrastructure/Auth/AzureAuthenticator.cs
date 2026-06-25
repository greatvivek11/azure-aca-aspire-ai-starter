using System.Net.Http.Headers;
using System.Text.Json;

internal sealed class AzureAuthenticator(IHttpClientFactory httpClientFactory)
{
    public async Task<bool> ConfigureOpenAiAuthAsync(
        HttpClient client,
        string? apiKey,
        string openAiAuthMode,
        string? managedIdentityClientId)
    {
        if (string.Equals(openAiAuthMode, "managed-identity", StringComparison.OrdinalIgnoreCase))
        {
            var token = await TryAcquireManagedIdentityTokenAsync(
                "https://cognitiveservices.azure.com",
                managedIdentityClientId);
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

    public async Task<string?> TryAcquireManagedIdentityTokenAsync(string resource, string? managedIdentityClientId)
    {
        try
        {
            var identityEndpoint = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT");
            var identityHeader = Environment.GetEnvironmentVariable("IDENTITY_HEADER");
            if (string.IsNullOrWhiteSpace(identityEndpoint) || string.IsNullOrWhiteSpace(identityHeader))
            {
                return null;
            }

            var requestUri = $"{identityEndpoint}?api-version=2019-08-01&resource={Uri.EscapeDataString(resource)}";
            if (!string.IsNullOrWhiteSpace(managedIdentityClientId))
            {
                requestUri = $"{requestUri}&client_id={Uri.EscapeDataString(managedIdentityClientId)}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Add("X-IDENTITY-HEADER", identityHeader);

            using var client = httpClientFactory.CreateClient();
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("access_token", out var tokenElement))
            {
                return null;
            }

            return tokenElement.GetString();
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

    public static bool IsAuthFailure(System.Net.HttpStatusCode statusCode) =>
        statusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden;
}
