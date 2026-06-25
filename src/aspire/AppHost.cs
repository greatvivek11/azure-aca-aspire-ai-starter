using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Dapr;
using CommunityToolkit.Aspire.Hosting.Dapr;
using System.Collections.Immutable;
using System;
using System.IO;

var builder = DistributedApplication.CreateBuilder(args);

// Detect if running in devcontainer
bool isInDevcontainer = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DEVCONTAINERS")) || 
                        File.Exists("/.dockerenv");

// Keep Dapr components repo-scoped so local runtime does not load ~/.dapr defaults.
var daprComponentsPath = Path.GetFullPath("../components");
Directory.CreateDirectory(daprComponentsPath);
Environment.SetEnvironmentVariable("DAPR_COMPONENTS_PATH", daprComponentsPath);

// Load environment variables from .env file
LoadEnvFile(".env");

// Centralized configuration management
var azureOpenAiApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ?? string.Empty;
var azureOpenAiModelId = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_ID") ?? string.Empty;
var azureOpenAiEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? string.Empty;
var aiMode = (Environment.GetEnvironmentVariable("AI_MODE") ?? "local").Trim().ToLowerInvariant();

// Set appropriate llama.cpp URLs based on environment
var localLlmBaseUrl = isInDevcontainer 
    ? "http://llama-chat:8080"
    : GetEnvOrDefault("LLAMA_CPP_BASE_URL", "http://host.docker.internal:8082");
var localLlmEmbedBaseUrl = isInDevcontainer
    ? "http://llama-embed:8080"
    : GetEnvOrDefault("LLAMA_CPP_EMBED_BASE_URL", "http://host.docker.internal:8083");
var localLlmChatModel = GetEnvOrDefault("LLAMA_CPP_CHAT_MODEL", "Qwen/Qwen2.5-0.5B-Instruct");
var localLlmEmbedModel = GetEnvOrDefault("LLAMA_CPP_EMBED_MODEL", "nomic-embed-text");
var localLlmEmbedDimensions = GetEnvOrDefault("LLAMA_CPP_EMBED_DIMENSIONS", "768");
var qdrantUrl = Environment.GetEnvironmentVariable("QDRANT_URL") ?? "http://qdrant:6333";
var qdrantCollection = Environment.GetEnvironmentVariable("QDRANT_COLLECTION") ?? "documents";
var azureOpenAiAuthMode = Environment.GetEnvironmentVariable("AZURE_OPENAI_AUTH_MODE") ?? "api-key";
var azureOpenAiEmbeddingModelId = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_MODEL_ID") ?? "text-embedding-3-small";
var azureOpenAiEmbeddingDimensions = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DIMENSIONS") ?? "1536";
var azureStorageAccountName = Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_NAME") ?? string.Empty;
var azureStorageConnectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING") ?? string.Empty;
var azureStorageContainerName = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONTAINER_NAME") ?? "documents";
var azureStorageAuthMode = Environment.GetEnvironmentVariable("AZURE_STORAGE_AUTH_MODE") ?? "managed-identity";
var storagePublicBlobEndpoint = Environment.GetEnvironmentVariable("AZURE_STORAGE_PUBLIC_BLOB_ENDPOINT") ?? string.Empty;
const string azuriteAccountName = "devstoreaccount1";
const string azuriteAccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
var defaultAzuriteInternalConnectionString =
    $"DefaultEndpointsProtocol=http;AccountName={azuriteAccountName};AccountKey={azuriteAccountKey};BlobEndpoint=http://azurite:10000/{azuriteAccountName};";
var defaultAzuritePublicBlobEndpoint = $"http://localhost:10000/{azuriteAccountName}";
var resolvedStorageAccountName = aiMode == "local" && string.IsNullOrWhiteSpace(azureStorageAccountName)
    ? azuriteAccountName
    : azureStorageAccountName;
var resolvedStorageConnectionString = aiMode == "local" &&
                                      (string.IsNullOrWhiteSpace(azureStorageConnectionString)
                                       || azureStorageConnectionString.Contains("AccountName=devstoreaccount1", StringComparison.OrdinalIgnoreCase))
    ? defaultAzuriteInternalConnectionString
    : azureStorageConnectionString;
var resolvedStorageAuthMode = aiMode == "local"
    ? "api-key"
    : azureStorageAuthMode;
var resolvedStoragePublicBlobEndpoint = aiMode == "local" && string.IsNullOrWhiteSpace(storagePublicBlobEndpoint)
    ? defaultAzuritePublicBlobEndpoint
    : storagePublicBlobEndpoint;
var azureSearchEndpoint = Environment.GetEnvironmentVariable("AZURE_SEARCH_ENDPOINT") ?? string.Empty;
var azureSearchIndexName = Environment.GetEnvironmentVariable("AZURE_SEARCH_INDEX_NAME") ?? "documents-index";
var azureSearchApiKey = Environment.GetEnvironmentVariable("AZURE_SEARCH_API_KEY") ?? string.Empty;
var azureSearchAuthMode = Environment.GetEnvironmentVariable("AZURE_SEARCH_AUTH_MODE") ?? "api-key";
var entraAuthEnabled = Environment.GetEnvironmentVariable("ENTRA_AUTH_ENABLED") ?? "false";
var entraTenantId = Environment.GetEnvironmentVariable("ENTRA_TENANT_ID") ?? string.Empty;
var entraAuthority = Environment.GetEnvironmentVariable("ENTRA_AUTHORITY") ?? string.Empty;
var entraApiClientId = Environment.GetEnvironmentVariable("ENTRA_API_CLIENT_ID") ?? string.Empty;
var entraAudience = Environment.GetEnvironmentVariable("ENTRA_AUDIENCE") ?? string.Empty;
var entraSpaClientId = Environment.GetEnvironmentVariable("ENTRA_SPA_CLIENT_ID") ?? string.Empty;
var entraScope = Environment.GetEnvironmentVariable("ENTRA_SCOPE") ?? string.Empty;
var frontendMode = (Environment.GetEnvironmentVariable("ASPIRE_FRONTEND_MODE") ?? "container")
    .Trim()
    .ToLowerInvariant();
var sqlSaPassword = "P@ssw0rd";
var sqlImage = Environment.GetEnvironmentVariable("SQL_IMAGE") ?? "mcr.microsoft.com/azure-sql-edge:latest";

// In devcontainer: add llama.cpp containers with model volumes
IResourceBuilder<ContainerResource>? llamaChatResource = null;
IResourceBuilder<ContainerResource>? llamaEmbedResource = null;

if (isInDevcontainer && aiMode == "local")
{
    var modelsDir = Environment.GetEnvironmentVariable("LLAMA_CPP_DEVCONTAINER_MODELS_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "llama.cpp", "models");
    Directory.CreateDirectory(modelsDir);
    
    var llamaChatImage = Environment.GetEnvironmentVariable("LLAMA_CPP_CHAT_IMAGE") ?? "ghcr.io/ggml-org/llama.cpp:server";
    var llamaEmbedImage = Environment.GetEnvironmentVariable("LLAMA_CPP_EMBED_IMAGE") ?? "ghcr.io/ggml-org/llama.cpp:server";
    var chatModel = Environment.GetEnvironmentVariable("LLAMA_CPP_CHAT_MODEL_FILE") ?? "Qwen2.5-0.5B-Instruct-Q4_K_M.gguf";
    var embedModel = Environment.GetEnvironmentVariable("LLAMA_CPP_EMBED_MODEL_FILE") ?? "nomic-embed-text-v1.5.f16.gguf";
    
    llamaChatResource = builder.AddContainer("llama-chat", llamaChatImage)
        .WithBindMount(modelsDir, "/models", isReadOnly: true)
        .WithHttpEndpoint(name: "http", targetPort: 8080)
        .WithArgs("--host", "0.0.0.0", "--port", "8080", "--model", $"/models/{chatModel}", "--alias", localLlmChatModel);
    
    llamaEmbedResource = builder.AddContainer("llama-embed", llamaEmbedImage)
        .WithBindMount(modelsDir, "/models", isReadOnly: true)
        .WithHttpEndpoint(name: "http", targetPort: 8080)
        .WithArgs("--host", "0.0.0.0", "--port", "8080", "--model", $"/models/{embedModel}", "--alias", localLlmEmbedModel, "--embedding");
}

// Local infrastructure containers are orchestrated by Aspire; llama.cpp runs natively on the host.
var sql = builder.AddContainer("sql", sqlImage)
    .WithEnvironment("ACCEPT_EULA", "Y")
    .WithEnvironment("MSSQL_PID", "Developer")
    .WithEnvironment("SA_PASSWORD", sqlSaPassword)
    .WithEnvironment("MSSQL_SA_PASSWORD", sqlSaPassword)
    .WithEndpoint(name: "tcp", port: 1433, targetPort: 1433)
    .WithVolume("mssql-data-v1", "/var/opt/mssql");

var redis = builder.AddContainer("redis", "redis:7-alpine")
    .WithEndpoint(name: "tcp", port: 6379, targetPort: 6379)
    .WithVolume("redis-data", "/data");

var azurite = builder.AddContainer("azurite", "mcr.microsoft.com/azure-storage/azurite:latest")
    .WithEndpoint(name: "blob", port: 10000, targetPort: 10000)
    .WithVolume("azurite-data", "/data");

var qdrant = builder.AddContainer("qdrant", "qdrant/qdrant:v1.12.6")
    .WithEndpoint(name: "http", port: 6333, targetPort: 6333)
    .WithVolume("qdrant-data", "/qdrant/storage");

// Parameter values will be read from appsettings.json or environment variables
// You can also set them through the .NET Aspire dashboard when running the app

// Add existing projects with Dapr support and Dockerfiles
var backendBuilder = builder.AddDockerfile("backend", "../backend")
    .WaitFor(sql)
    .WaitFor(redis)
    .WaitFor(azurite)
    .WaitFor(qdrant);

// Add wait dependencies for llama.cpp containers in devcontainer
if (isInDevcontainer && aiMode == "local" && llamaChatResource != null && llamaEmbedResource != null)
{
    backendBuilder.WaitFor(llamaChatResource);
    backendBuilder.WaitFor(llamaEmbedResource);
}
var backend = backendBuilder
    .WithOtlpExporter()
    .WithHttpEndpoint(name: "http", port: 8080, targetPort: 8080)
    .WithDaprSidecar(new CommunityToolkit.Aspire.Hosting.Dapr.DaprSidecarOptions
    {
        AppId = "api",
        AppPort = 8080,
        ResourcesPaths = ImmutableHashSet.Create(daprComponentsPath)
    })
    .WithEnvironment("AZURE_OPENAI_API_KEY", azureOpenAiApiKey)
    .WithEnvironment("AZURE_OPENAI_MODEL_ID", azureOpenAiModelId)
    .WithEnvironment("AZURE_OPENAI_ENDPOINT", azureOpenAiEndpoint)
    .WithEnvironment("AI_MODE", aiMode)
    .WithEnvironment("LLAMA_CPP_BASE_URL", localLlmBaseUrl)
    .WithEnvironment("LLAMA_CPP_EMBED_BASE_URL", localLlmEmbedBaseUrl)
    .WithEnvironment("LLAMA_CPP_CHAT_MODEL", localLlmChatModel)
    .WithEnvironment("LLAMA_CPP_EMBED_MODEL", localLlmEmbedModel)
    .WithEnvironment("LLAMA_CPP_EMBED_DIMENSIONS", localLlmEmbedDimensions)
    .WithEnvironment("QDRANT_URL", qdrantUrl)
    .WithEnvironment("QDRANT_COLLECTION", qdrantCollection)
    .WithEnvironment("AZURE_OPENAI_AUTH_MODE", azureOpenAiAuthMode)
    .WithEnvironment("AZURE_OPENAI_EMBEDDING_MODEL_ID", azureOpenAiEmbeddingModelId)
    .WithEnvironment("AZURE_OPENAI_EMBEDDING_DIMENSIONS", azureOpenAiEmbeddingDimensions)
    .WithEnvironment("AZURE_STORAGE_ACCOUNT_NAME", resolvedStorageAccountName)
    .WithEnvironment("AZURE_STORAGE_CONNECTION_STRING", resolvedStorageConnectionString)
    .WithEnvironment("AZURE_STORAGE_CONTAINER_NAME", azureStorageContainerName)
    .WithEnvironment("AZURE_STORAGE_AUTH_MODE", resolvedStorageAuthMode)
    .WithEnvironment("AZURE_STORAGE_PUBLIC_BLOB_ENDPOINT", resolvedStoragePublicBlobEndpoint)
    .WithEnvironment("AZURE_SEARCH_ENDPOINT", azureSearchEndpoint)
    .WithEnvironment("AZURE_SEARCH_INDEX_NAME", azureSearchIndexName)
    .WithEnvironment("AZURE_SEARCH_API_KEY", azureSearchApiKey)
    .WithEnvironment("AZURE_SEARCH_AUTH_MODE", azureSearchAuthMode)
    .WithEnvironment("ENTRA_AUTH_ENABLED", entraAuthEnabled)
    .WithEnvironment("ENTRA_TENANT_ID", entraTenantId)
    .WithEnvironment("ENTRA_AUTHORITY", entraAuthority)
    .WithEnvironment("ENTRA_API_CLIENT_ID", entraApiClientId)
    .WithEnvironment("ENTRA_AUDIENCE", entraAudience)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WithEnvironment("WORKER_DAPR_BASE_URL", "http://localhost:3500/v1.0/invoke/worker/method")
    .WithEnvironment("ConnectionStrings__SqlServer", "Server=sql,1433;Database=AcaAspireAiTemplate;User Id=sa;Password=P@ssw0rd;TrustServerCertificate=true")
    .WithEnvironment("ConnectionStrings__Redis", "redis:6379")
    .WithEnvironment("REDIS_CONNECTION_STRING", "redis:6379");

var workerBuilder = builder.AddDockerfile("worker", "../worker")
    .WaitFor(sql)
    .WaitFor(redis)
    .WaitFor(azurite)
    .WaitFor(qdrant);

// Add wait dependencies for llama.cpp containers in devcontainer
if (isInDevcontainer && aiMode == "local" && llamaChatResource != null && llamaEmbedResource != null)
{
    workerBuilder.WaitFor(llamaChatResource);
    workerBuilder.WaitFor(llamaEmbedResource);
}
var worker = workerBuilder
    .WithOtlpExporter()
    .WithHttpEndpoint(name: "http", port: 8081, targetPort: 8081)
    .WithDaprSidecar(new CommunityToolkit.Aspire.Hosting.Dapr.DaprSidecarOptions
    {
        AppId = "worker",
        AppPort = 8081,
        ResourcesPaths = ImmutableHashSet.Create(daprComponentsPath)
    })
    .WithEnvironment("ConnectionStrings__SqlServer", "Server=sql,1433;Database=AcaAspireAiTemplate;User Id=sa;Password=P@ssw0rd;TrustServerCertificate=true")
    .WithEnvironment("ConnectionStrings__Redis", "redis:6379")
    .WithEnvironment("REDIS_CONNECTION_STRING", "redis:6379")
    .WithEnvironment("AZURE_OPENAI_API_KEY", azureOpenAiApiKey)
    .WithEnvironment("AZURE_OPENAI_MODEL_ID", azureOpenAiModelId)
    .WithEnvironment("AZURE_OPENAI_ENDPOINT", azureOpenAiEndpoint)
    .WithEnvironment("AI_MODE", aiMode)
    .WithEnvironment("LLAMA_CPP_BASE_URL", localLlmBaseUrl)
    .WithEnvironment("LLAMA_CPP_EMBED_BASE_URL", localLlmEmbedBaseUrl)
    .WithEnvironment("LLAMA_CPP_CHAT_MODEL", localLlmChatModel)
    .WithEnvironment("LLAMA_CPP_EMBED_MODEL", localLlmEmbedModel)
    .WithEnvironment("LLAMA_CPP_EMBED_DIMENSIONS", localLlmEmbedDimensions)
    .WithEnvironment("QDRANT_URL", qdrantUrl)
    .WithEnvironment("QDRANT_COLLECTION", qdrantCollection)
    .WithEnvironment("AZURE_OPENAI_AUTH_MODE", azureOpenAiAuthMode)
    .WithEnvironment("AZURE_OPENAI_EMBEDDING_MODEL_ID", azureOpenAiEmbeddingModelId)
    .WithEnvironment("AZURE_OPENAI_EMBEDDING_DIMENSIONS", azureOpenAiEmbeddingDimensions)
    .WithEnvironment("AZURE_STORAGE_ACCOUNT_NAME", resolvedStorageAccountName)
    .WithEnvironment("AZURE_STORAGE_CONNECTION_STRING", resolvedStorageConnectionString)
    .WithEnvironment("AZURE_STORAGE_CONTAINER_NAME", azureStorageContainerName)
    .WithEnvironment("AZURE_STORAGE_AUTH_MODE", resolvedStorageAuthMode)
    .WithEnvironment("AZURE_SEARCH_ENDPOINT", azureSearchEndpoint)
    .WithEnvironment("AZURE_SEARCH_INDEX_NAME", azureSearchIndexName)
    .WithEnvironment("AZURE_SEARCH_API_KEY", azureSearchApiKey)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development");

if (frontendMode == "vite-dev")
{
    var frontendDev = builder.AddNpmApp("frontend", "../frontend", "dev:aspire")
        .WaitFor(backend)
        .WaitFor(redis)
        .WithOtlpExporter()
        .WithHttpEndpoint(name: "http", port: 3000, env: "PORT")
        .WithDaprSidecar(new CommunityToolkit.Aspire.Hosting.Dapr.DaprSidecarOptions
        {
            AppId = "web",
            AppPort = 3000,
            ResourcesPaths = ImmutableHashSet.Create(daprComponentsPath)
        })
        .WithEnvironment("BACKEND_API_BASE_URL", "http://localhost:8080")
        .WithEnvironment("AI_MODE", aiMode)
        .WithEnvironment("ENTRA_AUTH_ENABLED", entraAuthEnabled)
        .WithEnvironment("ENTRA_TENANT_ID", entraTenantId)
        .WithEnvironment("ENTRA_AUTHORITY", entraAuthority)
        .WithEnvironment("ENTRA_API_CLIENT_ID", entraApiClientId)
        .WithEnvironment("ENTRA_SPA_CLIENT_ID", entraSpaClientId)
        .WithEnvironment("ENTRA_SCOPE", entraScope)
        .WithEnvironment("REDIS_URL", "redis://redis:6379");
}
else
{
    var frontend = builder.AddDockerfile("frontend", "../frontend")
        .WaitFor(backend)
        .WaitFor(redis)
        .WithOtlpExporter()
        .WithHttpEndpoint(name: "http", port: 3000, targetPort: 3000)
        .WithDaprSidecar(new CommunityToolkit.Aspire.Hosting.Dapr.DaprSidecarOptions
        {
            AppId = "web",
            AppPort = 3000,
            ResourcesPaths = ImmutableHashSet.Create(daprComponentsPath)
        })
        .WithEnvironment("BACKEND_API_BASE_URL", "http://backend:8080")
        .WithEnvironment("BACKEND_DAPR_BASE_URL", "http://localhost:3500/v1.0/invoke/api/method")
        .WithEnvironment("AI_MODE", aiMode)
        .WithEnvironment("ENTRA_AUTH_ENABLED", entraAuthEnabled)
        .WithEnvironment("ENTRA_TENANT_ID", entraTenantId)
        .WithEnvironment("ENTRA_AUTHORITY", entraAuthority)
        .WithEnvironment("ENTRA_API_CLIENT_ID", entraApiClientId)
        .WithEnvironment("ENTRA_SPA_CLIENT_ID", entraSpaClientId)
        .WithEnvironment("ENTRA_SCOPE", entraScope)
        .WithEnvironment("REDIS_URL", "redis://redis:6379");
}

builder.Build().Run();

// Helper method to load .env file
static void LoadEnvFile(string filePath)
{
    if (!File.Exists(filePath))
        return;

    foreach (var line in File.ReadAllLines(filePath))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
            continue;

        var parts = line.Split('=', 2);
        if (parts.Length == 2)
        {
            var key = parts[0].Trim();
            var existingValue = Environment.GetEnvironmentVariable(key);

            // Respect launch profile / parent process variables (for example VS Code F5)
            // and only backfill from .env when the variable is not already set.
            if (!string.IsNullOrWhiteSpace(existingValue))
            {
                continue;
            }

            Environment.SetEnvironmentVariable(key, parts[1].Trim());
        }
    }
}

static string GetEnvOrDefault(string name, string defaultValue)
{
    var value = Environment.GetEnvironmentVariable(name);
    return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
}
