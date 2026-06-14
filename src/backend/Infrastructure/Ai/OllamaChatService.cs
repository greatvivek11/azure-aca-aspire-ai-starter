using System.Text;
using System.Text.Json;

namespace AcaAspireAiTemplate.Backend.Infrastructure.Ai;

public sealed class OllamaChatService : IAiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;
    private readonly string _chatModel;

    public OllamaChatService(
        IHttpClientFactory httpClientFactory,
        string baseUrl,
        string chatModel)
    {
        _httpClientFactory = httpClientFactory;
        _baseUrl = baseUrl.TrimEnd('/');
        _chatModel = chatModel;
    }

    public async Task<string> InvokePromptAsync(string prompt)
    {
        using var client = _httpClientFactory.CreateClient();
        await EnsureModelPulledAsync(client, _baseUrl, _chatModel);

        var requestBody = new
        {
            model = _chatModel,
            stream = false,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = prompt
                }
            },
            options = new
            {
                temperature = 0.2
            }
        };

        using var payload = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync($"{_baseUrl}/api/chat", payload);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("message", out var messageElement)
            && messageElement.TryGetProperty("content", out var contentElement))
        {
            return contentElement.GetString()?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }

    private static async Task EnsureModelPulledAsync(HttpClient client, string ollamaBaseUrl, string modelName)
    {
        var pullPayload = JsonSerializer.Serialize(new { name = modelName, stream = false });
        using var content = new StringContent(pullPayload, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync($"{ollamaBaseUrl}/api/pull", content);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Ollama failed to pull model '{modelName}' (HTTP {(int)response.StatusCode}). Response: {responseBody}");
        }
    }
}
