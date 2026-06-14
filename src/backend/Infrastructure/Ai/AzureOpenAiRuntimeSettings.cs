namespace AcaAspireAiTemplate.Backend.Infrastructure.Ai;

public sealed record AzureOpenAiRuntimeSettings(string ApiKey, string ModelId, Uri Endpoint);
