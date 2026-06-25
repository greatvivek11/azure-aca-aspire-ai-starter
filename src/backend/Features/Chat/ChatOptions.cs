using AcaAspireAiTemplate.Backend.Infrastructure.Ai;

namespace AcaAspireAiTemplate.Backend.Features.Chat;

public sealed record ChatOptions(
    string AiMode,
    AzureOpenAiRuntimeSettings? AzureOpenAi,
    string OpenAiAuthMode,
    string EmbeddingModelId,
    int EmbeddingDimensions,
    string LocalLlmEmbedBaseUrl,
    string QdrantUrl,
    string QdrantCollection,
    string SearchEndpoint,
    string SearchIndexName,
    string? SearchApiKey,
    string? ManagedIdentityClientId,
    bool UseManagedIdentity,
    bool LocalRagFastResponse = false,
    int LocalRagTopK = 3,
    int LocalRagMaxContextCharacters = 1800,
    int LocalRagMaxChunkCharacters = 700)
{
    public bool IsLocalMode => string.Equals(AiMode, "local", StringComparison.OrdinalIgnoreCase);

    public bool ManagedIdentityRuntimeAvailable =>
        UseManagedIdentity &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT")) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IDENTITY_HEADER"));

    public bool SearchConfigured =>
        IsLocalMode
            ? !string.IsNullOrWhiteSpace(QdrantUrl) && !string.IsNullOrWhiteSpace(QdrantCollection)
            : !string.IsNullOrWhiteSpace(SearchEndpoint) &&
              !string.IsNullOrWhiteSpace(SearchIndexName) &&
              (ManagedIdentityRuntimeAvailable || !string.IsNullOrWhiteSpace(SearchApiKey));
}
