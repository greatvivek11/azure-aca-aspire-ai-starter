namespace AIHub.Backend.Infrastructure.Ai;

public sealed record AzureOpenAiRuntimeSettings(string ApiKey, string ModelId, Uri Endpoint);
