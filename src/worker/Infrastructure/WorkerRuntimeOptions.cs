using Microsoft.Extensions.Configuration;

internal sealed record WorkerRuntimeOptions(
    string AiMode,
    string SqlConnectionString,
    string StorageAccountName,
    string StorageConnectionString,
    string StorageContainerName,
    string StorageAuthMode,
    string SearchEndpoint,
    string SearchIndexName,
    string SearchApiKey,
    string QdrantUrl,
    string QdrantCollection,
    string OllamaBaseUrl,
    string OpenAiEndpointText,
    string OpenAiApiKey,
    string OpenAiAuthMode,
    string? ManagedIdentityClientId,
    string EmbeddingModelId,
    int EmbeddingDimensions,
    bool StorageConfigured,
    bool AzureIngestionConfigured,
    bool LocalIngestionConfigured,
    bool IngestionConfigured)
{
    public static WorkerRuntimeOptions FromEnvironment(IConfiguration configuration)
    {
        var sqlConnectionString = GetSqlConnectionString(configuration);
        var aiMode = (Environment.GetEnvironmentVariable("AI_MODE") ?? "azure").Trim().ToLowerInvariant();

        var storageAccountName = Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_NAME") ?? string.Empty;
        var storageConnectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING") ?? string.Empty;
        var storageContainerName = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONTAINER_NAME") ?? string.Empty;
        var storageAuthMode = (Environment.GetEnvironmentVariable("AZURE_STORAGE_AUTH_MODE") ?? "managed-identity")
            .Trim()
            .ToLowerInvariant();

        var searchEndpoint = Environment.GetEnvironmentVariable("AZURE_SEARCH_ENDPOINT") ?? string.Empty;
        var searchIndexName = Environment.GetEnvironmentVariable("AZURE_SEARCH_INDEX_NAME") ?? string.Empty;
        var searchApiKey = Environment.GetEnvironmentVariable("AZURE_SEARCH_API_KEY") ?? string.Empty;

        var qdrantUrl = (Environment.GetEnvironmentVariable("QDRANT_URL") ?? "http://qdrant:6333").Trim();
        var qdrantCollection = (Environment.GetEnvironmentVariable("QDRANT_COLLECTION") ?? "documents").Trim();

        var ollamaBaseUrl = (Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://ollama:11434").Trim();

        var openAiEndpointText = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? string.Empty;
        var openAiApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ?? string.Empty;
        var openAiAuthMode = (Environment.GetEnvironmentVariable("AZURE_OPENAI_AUTH_MODE") ?? "api-key")
            .Trim()
            .ToLowerInvariant();
        var managedIdentityClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");

        var embeddingModelId = aiMode == "local"
            ? (Environment.GetEnvironmentVariable("OLLAMA_EMBED_MODEL") ?? "nomic-embed-text")
            : (Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_MODEL_ID") ?? string.Empty);

        var embeddingDimensions = aiMode == "local"
            ? (int.TryParse(Environment.GetEnvironmentVariable("OLLAMA_EMBED_DIMENSIONS"), out var parsedLocalDimensions)
                ? parsedLocalDimensions
                : 768)
            : (int.TryParse(Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DIMENSIONS"), out var parsedAzureDimensions)
                ? parsedAzureDimensions
                : 1536);

        var storageConfigured =
            (!string.IsNullOrWhiteSpace(storageConnectionString) || !string.IsNullOrWhiteSpace(storageAccountName))
            && !string.IsNullOrWhiteSpace(storageContainerName);

        var azureIngestionConfigured =
            storageConfigured &&
            !string.IsNullOrWhiteSpace(searchEndpoint) &&
            !string.IsNullOrWhiteSpace(searchIndexName) &&
            !string.IsNullOrWhiteSpace(searchApiKey) &&
            !string.IsNullOrWhiteSpace(openAiEndpointText) &&
            (!string.Equals(openAiAuthMode, "api-key", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(openAiApiKey)) &&
            !string.IsNullOrWhiteSpace(embeddingModelId);

        var localIngestionConfigured =
            storageConfigured &&
            !string.IsNullOrWhiteSpace(qdrantUrl) &&
            !string.IsNullOrWhiteSpace(qdrantCollection) &&
            !string.IsNullOrWhiteSpace(ollamaBaseUrl) &&
            !string.IsNullOrWhiteSpace(embeddingModelId);

        var ingestionConfigured = aiMode == "local" ? localIngestionConfigured : azureIngestionConfigured;

        return new WorkerRuntimeOptions(
            aiMode,
            sqlConnectionString,
            storageAccountName,
            storageConnectionString,
            storageContainerName,
            storageAuthMode,
            searchEndpoint,
            searchIndexName,
            searchApiKey,
            qdrantUrl,
            qdrantCollection,
            ollamaBaseUrl,
            openAiEndpointText,
            openAiApiKey,
            openAiAuthMode,
            managedIdentityClientId,
            embeddingModelId,
            embeddingDimensions,
            storageConfigured,
            azureIngestionConfigured,
            localIngestionConfigured,
            ingestionConfigured);
    }

    public Uri GetOpenAiEndpointOrFallback()
    {
        return string.IsNullOrWhiteSpace(OpenAiEndpointText)
            ? new Uri("http://localhost")
            : new Uri(OpenAiEndpointText, UriKind.Absolute);
    }

    private static string GetSqlConnectionString(IConfiguration configuration)
    {
        var explicitConnectionString = configuration.GetConnectionString("SqlServer")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__SqlServer");
        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            return explicitConnectionString;
        }

        var sqlServer = Environment.GetEnvironmentVariable("SQL_SERVER");
        var sqlDatabase = Environment.GetEnvironmentVariable("SQL_DATABASE");
        var uamiClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(sqlServer)) missing.Add("SQL_SERVER");
        if (string.IsNullOrWhiteSpace(sqlDatabase)) missing.Add("SQL_DATABASE");
        if (string.IsNullOrWhiteSpace(uamiClientId)) missing.Add("AZURE_CLIENT_ID");
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"SQL configuration is incomplete. Missing: {string.Join(", ", missing)}.");
        }

        return $"Server=tcp:{sqlServer},1433;Initial Catalog={sqlDatabase};"
             + $"Authentication=Active Directory Managed Identity;User Id={uamiClientId};"
             + "Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";
    }
}
