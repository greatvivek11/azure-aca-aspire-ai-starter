using AcaAspireAiTemplate.Backend.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;

namespace AcaAspireAiTemplate.Backend.Infrastructure.Startup;

public sealed record BackendRuntimeOptions(
    string AiMode,
    string LocalLlmBaseUrl,
    string LocalLlmEmbedBaseUrl,
    string LocalLlmChatModel,
    string LocalLlmEmbedModel,
    string QdrantUrl,
    string QdrantCollection,
    AzureOpenAiRuntimeSettings? AzureOpenAiSettings,
    string OpenAiAuthMode,
    string? ManagedIdentityClientId,
    string SqlConnectionString,
    string WorkerDaprBaseUrl,
    string StorageConnectionString,
    string StorageContainerName,
    string StoragePublicBlobEndpoint,
    string SearchEndpoint,
    string SearchIndexName,
    string? SearchApiKey,
    bool UseManagedIdentityForSearch,
    bool LocalRagFastResponse,
    int LocalRagTopK,
    int LocalRagMaxContextCharacters,
    int LocalRagMaxChunkCharacters,
    int LocalLlmChatTimeoutSeconds,
    int LocalLlmChatMaxTokens,
    string StorageAccountName,
    string StorageAuthMode,
    string EmbeddingModelId,
    int EmbeddingDimensions,
    int UploadUrlExpiryMinutes,
    long UploadMaxRequestBytes)
{
    public static BackendRuntimeOptions FromConfiguration(IConfiguration configuration)
    {
        var aiMode = (Environment.GetEnvironmentVariable("AI_MODE") ?? "azure").Trim().ToLowerInvariant();
        var localLlmBaseUrl = (Environment.GetEnvironmentVariable("LLAMA_CPP_BASE_URL") ?? "http://host.docker.internal:8082").Trim();
        var localLlmEmbedBaseUrl = (Environment.GetEnvironmentVariable("LLAMA_CPP_EMBED_BASE_URL") ?? "http://host.docker.internal:8083").Trim();
        var localLlmChatModel = (Environment.GetEnvironmentVariable("LLAMA_CPP_CHAT_MODEL") ?? "Qwen/Qwen2.5-0.5B-Instruct").Trim();
        var localLlmEmbedModel = (Environment.GetEnvironmentVariable("LLAMA_CPP_EMBED_MODEL") ?? "nomic-embed-text").Trim();
        var qdrantUrl = (Environment.GetEnvironmentVariable("QDRANT_URL") ?? "http://qdrant:6333").Trim();
        var qdrantCollection = (Environment.GetEnvironmentVariable("QDRANT_COLLECTION") ?? "documents").Trim();

        var azureOpenAiSettings = aiMode == "azure"
            ? ResolveAzureOpenAiSettings(configuration)
            : null;

        var openAiAuthMode = (Environment.GetEnvironmentVariable("AZURE_OPENAI_AUTH_MODE") ?? "api-key").Trim().ToLowerInvariant();
        var managedIdentityClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");

        var sqlConnectionString = ResolveSqlConnectionString(configuration);

        var workerDaprBaseUrl = Environment.GetEnvironmentVariable("WORKER_DAPR_BASE_URL")
            ?? "http://localhost:3500/v1.0/invoke/worker/method";

        var storageConnectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING") ?? string.Empty;
        var storageContainerName = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONTAINER_NAME") ?? string.Empty;
        var storagePublicBlobEndpoint = Environment.GetEnvironmentVariable("AZURE_STORAGE_PUBLIC_BLOB_ENDPOINT") ?? string.Empty;

        var searchEndpoint = Environment.GetEnvironmentVariable("AZURE_SEARCH_ENDPOINT") ?? string.Empty;
        var searchIndexName = Environment.GetEnvironmentVariable("AZURE_SEARCH_INDEX_NAME") ?? string.Empty;
        var searchApiKey = Environment.GetEnvironmentVariable("AZURE_SEARCH_API_KEY");
        var useManagedIdentityForSearch = string.Equals(
            Environment.GetEnvironmentVariable("AZURE_SEARCH_AUTH_MODE"),
            "managed-identity",
            StringComparison.OrdinalIgnoreCase);

        var localRagFastResponse = string.Equals(
            Environment.GetEnvironmentVariable("LOCAL_RAG_FAST_RESPONSE"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        var localRagTopK = ReadClampedInt("LOCAL_RAG_TOP_K", 3, 1, 5);
        var localRagMaxContextCharacters = ReadClampedInt("LOCAL_RAG_MAX_CONTEXT_CHARS", 1800, 500, 6000);
        var localRagMaxChunkCharacters = ReadClampedInt("LOCAL_RAG_MAX_CHUNK_CHARS", 700, 250, 2000);
        var localLlmChatTimeoutSeconds = ReadClampedInt("LLAMA_CPP_CHAT_TIMEOUT_SECONDS", 600, 30, 900);
        var localLlmChatMaxTokens = ReadClampedInt("LLAMA_CPP_CHAT_MAX_TOKENS", 160, 32, 512);

        var storageAccountName = Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_NAME") ?? string.Empty;
        var storageAuthMode = (Environment.GetEnvironmentVariable("AZURE_STORAGE_AUTH_MODE") ?? "managed-identity")
            .Trim()
            .ToLowerInvariant();

        var embeddingModelId = (aiMode == "local"
            ? Environment.GetEnvironmentVariable("LLAMA_CPP_EMBED_MODEL") ?? "nomic-embed-text"
            : Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_MODEL_ID") ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(embeddingModelId) && aiMode == "azure")
        {
            throw new InvalidOperationException("AZURE_OPENAI_EMBEDDING_MODEL_ID is required for document grounding and must reference an embeddings deployment.");
        }

        var embeddingDimensions = aiMode == "local"
            ? (int.TryParse(Environment.GetEnvironmentVariable("LLAMA_CPP_EMBED_DIMENSIONS"), out var parsedLocalDimensions)
                ? parsedLocalDimensions
                : 768)
            : (int.TryParse(Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DIMENSIONS"), out var parsedDimensions)
                ? parsedDimensions
                : 1536);

        var uploadUrlExpiryMinutes = int.TryParse(Environment.GetEnvironmentVariable("UPLOAD_URL_EXPIRY_MINUTES"), out var parsedUploadExpiry)
            ? Math.Clamp(parsedUploadExpiry, 5, 120)
            : 15;

        var uploadMaxRequestBytes = long.TryParse(Environment.GetEnvironmentVariable("UPLOAD_MAX_REQUEST_BYTES"), out var parsedUploadMaxBytes)
            ? Math.Clamp(parsedUploadMaxBytes, 1_048_576, 104_857_600)
            : 26_214_400;

        return new BackendRuntimeOptions(
            aiMode,
            localLlmBaseUrl,
            localLlmEmbedBaseUrl,
            localLlmChatModel,
            localLlmEmbedModel,
            qdrantUrl,
            qdrantCollection,
            azureOpenAiSettings,
            openAiAuthMode,
            managedIdentityClientId,
            sqlConnectionString,
            workerDaprBaseUrl,
            storageConnectionString,
            storageContainerName,
            storagePublicBlobEndpoint,
            searchEndpoint,
            searchIndexName,
            searchApiKey,
            useManagedIdentityForSearch,
            localRagFastResponse,
            localRagTopK,
            localRagMaxContextCharacters,
            localRagMaxChunkCharacters,
            localLlmChatTimeoutSeconds,
            localLlmChatMaxTokens,
            storageAccountName,
            storageAuthMode,
            embeddingModelId,
            embeddingDimensions,
            uploadUrlExpiryMinutes,
            uploadMaxRequestBytes);
    }

    private static int ReadClampedInt(string name, int defaultValue, int minValue, int maxValue)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? Math.Clamp(value, minValue, maxValue)
            : defaultValue;
    }

    private static AzureOpenAiRuntimeSettings ResolveAzureOpenAiSettings(IConfiguration configuration)
    {
        var authMode = (Environment.GetEnvironmentVariable("AZURE_OPENAI_AUTH_MODE") ?? "api-key").Trim().ToLowerInvariant();
        var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
            ?? configuration[$"{AzureOpenAiOptions.SectionName}:ApiKey"];
        var modelId = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_ID")
            ?? configuration[$"{AzureOpenAiOptions.SectionName}:ModelId"];
        var endpointText = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
            ?? configuration[$"{AzureOpenAiOptions.SectionName}:Endpoint"];

        var issues = new List<string>();
        if (string.Equals(authMode, "api-key", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(apiKey))
        {
            issues.Add("AZURE_OPENAI_API_KEY is missing");
        }

        if (string.IsNullOrWhiteSpace(modelId)) issues.Add("AZURE_OPENAI_MODEL_ID is missing");
        if (string.IsNullOrWhiteSpace(endpointText)) issues.Add("AZURE_OPENAI_ENDPOINT is missing");
        if (!string.IsNullOrWhiteSpace(endpointText) && !Uri.TryCreate(endpointText, UriKind.Absolute, out _))
        {
            issues.Add("AZURE_OPENAI_ENDPOINT is not a valid absolute URI");
        }

        if (issues.Count > 0)
        {
            throw new InvalidOperationException(
                "Azure OpenAI configuration is invalid: "
                + string.Join("; ", issues)
                + ". Configure these values in Aspire parameters, environment variables, or appsettings.");
        }

        return new AzureOpenAiRuntimeSettings(apiKey!, modelId!, new Uri(endpointText!, UriKind.Absolute));
    }

    private static string ResolveSqlConnectionString(IConfiguration configuration)
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
            throw new InvalidOperationException(
                $"SQL configuration is incomplete. Missing: {string.Join(", ", missing)}.");
        }

        return $"Server=tcp:{sqlServer},1433;Initial Catalog={sqlDatabase};"
             + $"Authentication=Active Directory Managed Identity;User Id={uamiClientId};"
             + "Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";
    }
}
