using System.Text;
using System.Text.Json;

namespace AcaAspireAiTemplate.Backend.Infrastructure.Ai;

public sealed class LlamaCppChatService : IAiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;
    private readonly string _chatModel;
    private readonly int _timeoutSeconds;
    private readonly int _maxOutputTokens;

    public LlamaCppChatService(
        IHttpClientFactory httpClientFactory,
        string baseUrl,
        string chatModel,
        int timeoutSeconds = 600,
        int maxOutputTokens = 160)
    {
        _httpClientFactory = httpClientFactory;
        _baseUrl = baseUrl.TrimEnd('/');
        _chatModel = chatModel;
        _timeoutSeconds = timeoutSeconds;
        _maxOutputTokens = maxOutputTokens;
    }

    public async Task<string> InvokePromptAsync(string prompt)
    {
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);

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
            temperature = 0.2,
            max_tokens = _maxOutputTokens
        };

        using var payload = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync($"{_baseUrl}/v1/chat/completions", payload);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.TryGetProperty("choices", out var choicesElement)
            && choicesElement.ValueKind == JsonValueKind.Array
            && choicesElement.GetArrayLength() > 0)
        {
            var firstChoice = choicesElement[0];
            if (firstChoice.TryGetProperty("message", out var messageElement)
                && messageElement.TryGetProperty("content", out var contentElement))
            {
                return contentElement.GetString()?.Trim() ?? string.Empty;
            }
        }

        return string.Empty;
    }
}
