using Aspire.Hosting;
using Aspire.Hosting.Dapr;
using CommunityToolkit.Aspire.Hosting.Dapr;
using System.Collections.Immutable;
using System;
using System.IO;

var builder = DistributedApplication.CreateBuilder(args);

// Keep Dapr components repo-scoped so local runtime does not load ~/.dapr defaults.
var daprComponentsPath = Path.GetFullPath("../components");
Directory.CreateDirectory(daprComponentsPath);
Environment.SetEnvironmentVariable("DAPR_COMPONENTS_PATH", daprComponentsPath);

// Load environment variables from .env file
LoadEnvFile(".env");

// Centralized configuration management
var azureOpenAiApiKey = builder.AddParameter("azureOpenAiApiKey", secret: true);
var azureOpenAiModelId = builder.AddParameter("azureOpenAiModelId");
var azureOpenAiEndpoint = builder.AddParameter("azureOpenAiEndpoint");
var azureOpenAiAuthMode = Environment.GetEnvironmentVariable("AZURE_OPENAI_AUTH_MODE") ?? "api-key";
var azureOpenAiEmbeddingModelId = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_MODEL_ID") ?? "text-embedding-3-small";
var azureOpenAiEmbeddingDimensions = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DIMENSIONS") ?? "1536";
var azureStorageAccountName = Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_NAME") ?? string.Empty;
var azureStorageConnectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING") ?? string.Empty;
var azureStorageContainerName = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONTAINER_NAME") ?? "documents";
var azureStorageAuthMode = Environment.GetEnvironmentVariable("AZURE_STORAGE_AUTH_MODE") ?? "managed-identity";
var azureSearchEndpoint = Environment.GetEnvironmentVariable("AZURE_SEARCH_ENDPOINT") ?? string.Empty;
var azureSearchIndexName = Environment.GetEnvironmentVariable("AZURE_SEARCH_INDEX_NAME") ?? "documents-index";
var azureSearchApiKey = Environment.GetEnvironmentVariable("AZURE_SEARCH_API_KEY") ?? string.Empty;
var azureSearchAuthMode = Environment.GetEnvironmentVariable("AZURE_SEARCH_AUTH_MODE") ?? "api-key";
var frontendMode = (Environment.GetEnvironmentVariable("ASPIRE_FRONTEND_MODE") ?? "container")
    .Trim()
    .ToLowerInvariant();
var sqlSaPassword = "P@ssw0rd";
var sqlImage = Environment.GetEnvironmentVariable("SQL_IMAGE");
var resolvedSqlImage = string.IsNullOrWhiteSpace(sqlImage)
    ? "mcr.microsoft.com/mssql/server:2022-latest"
    : sqlImage;

// Local dependency containers are orchestrated by Aspire for host/devcontainer parity.
var sql = builder.AddContainer("sql", resolvedSqlImage)
    .WithEnvironment("ACCEPT_EULA", "Y")
    .WithEnvironment("MSSQL_PID", "Developer")
    .WithEnvironment("SA_PASSWORD", sqlSaPassword)
    .WithEnvironment("MSSQL_SA_PASSWORD", sqlSaPassword)
    .WithEndpoint(name: "tcp", port: 1433, targetPort: 1433)
    .WithVolume("mssql-data-v2", "/var/opt/mssql");

var redis = builder.AddContainer("redis", "redis:7-alpine")
    .WithEndpoint(name: "tcp", port: 6379, targetPort: 6379)
    .WithVolume("redis-data", "/data");

// Parameter values will be read from appsettings.json or environment variables
// You can also set them through the .NET Aspire dashboard when running the app

// Add existing projects with Dapr support and Dockerfiles
var backend = builder.AddDockerfile("backend", "../backend")
    .WaitFor(sql)
    .WaitFor(redis)
    .WithOtlpExporter()
    .WithHttpEndpoint(name: "http", port: 8080, targetPort: 8080)
    .WithDaprSidecar(new CommunityToolkit.Aspire.Hosting.Dapr.DaprSidecarOptions
    {
        AppId = "aihub-backend",
        AppPort = 8080,
        ResourcesPaths = ImmutableHashSet.Create(daprComponentsPath)
    })
    .WithEnvironment("AZURE_OPENAI_API_KEY", azureOpenAiApiKey)
    .WithEnvironment("AZURE_OPENAI_MODEL_ID", azureOpenAiModelId)
    .WithEnvironment("AZURE_OPENAI_ENDPOINT", azureOpenAiEndpoint)
    .WithEnvironment("AZURE_OPENAI_AUTH_MODE", azureOpenAiAuthMode)
    .WithEnvironment("AZURE_OPENAI_EMBEDDING_MODEL_ID", azureOpenAiEmbeddingModelId)
    .WithEnvironment("AZURE_OPENAI_EMBEDDING_DIMENSIONS", azureOpenAiEmbeddingDimensions)
    .WithEnvironment("AZURE_STORAGE_ACCOUNT_NAME", azureStorageAccountName)
    .WithEnvironment("AZURE_STORAGE_CONNECTION_STRING", azureStorageConnectionString)
    .WithEnvironment("AZURE_STORAGE_CONTAINER_NAME", azureStorageContainerName)
    .WithEnvironment("AZURE_STORAGE_AUTH_MODE", azureStorageAuthMode)
    .WithEnvironment("AZURE_SEARCH_ENDPOINT", azureSearchEndpoint)
    .WithEnvironment("AZURE_SEARCH_INDEX_NAME", azureSearchIndexName)
    .WithEnvironment("AZURE_SEARCH_API_KEY", azureSearchApiKey)
    .WithEnvironment("AZURE_SEARCH_AUTH_MODE", azureSearchAuthMode)
    .WithEnvironment("WORKER_DAPR_BASE_URL", "http://localhost:3500/v1.0/invoke/aihub-worker/method")
    .WithEnvironment("ConnectionStrings__SqlServer", "Server=sql,1433;Database=AIHub;User Id=sa;Password=P@ssw0rd;TrustServerCertificate=true")
    .WithEnvironment("ConnectionStrings__Redis", "redis:6379")
    .WithEnvironment("REDIS_CONNECTION_STRING", "redis:6379");

var worker = builder.AddDockerfile("worker", "../worker")
    .WaitFor(sql)
    .WaitFor(redis)
    .WithOtlpExporter()
    .WithHttpEndpoint(name: "http", port: 8081, targetPort: 8081)
    .WithDaprSidecar(new CommunityToolkit.Aspire.Hosting.Dapr.DaprSidecarOptions
    {
        AppId = "aihub-worker",
        AppPort = 8081,
        ResourcesPaths = ImmutableHashSet.Create(daprComponentsPath)
    })
    .WithEnvironment("ConnectionStrings__SqlServer", "Server=sql,1433;Database=AIHub;User Id=sa;Password=P@ssw0rd;TrustServerCertificate=true")
    .WithEnvironment("ConnectionStrings__Redis", "redis:6379")
    .WithEnvironment("REDIS_CONNECTION_STRING", "redis:6379")
    .WithEnvironment("AZURE_OPENAI_API_KEY", azureOpenAiApiKey)
    .WithEnvironment("AZURE_OPENAI_MODEL_ID", azureOpenAiModelId)
    .WithEnvironment("AZURE_OPENAI_ENDPOINT", azureOpenAiEndpoint)
    .WithEnvironment("AZURE_OPENAI_AUTH_MODE", azureOpenAiAuthMode)
    .WithEnvironment("AZURE_OPENAI_EMBEDDING_MODEL_ID", azureOpenAiEmbeddingModelId)
    .WithEnvironment("AZURE_OPENAI_EMBEDDING_DIMENSIONS", azureOpenAiEmbeddingDimensions)
    .WithEnvironment("AZURE_STORAGE_ACCOUNT_NAME", azureStorageAccountName)
    .WithEnvironment("AZURE_STORAGE_CONNECTION_STRING", azureStorageConnectionString)
    .WithEnvironment("AZURE_STORAGE_CONTAINER_NAME", azureStorageContainerName)
    .WithEnvironment("AZURE_STORAGE_AUTH_MODE", azureStorageAuthMode)
    .WithEnvironment("AZURE_SEARCH_ENDPOINT", azureSearchEndpoint)
    .WithEnvironment("AZURE_SEARCH_INDEX_NAME", azureSearchIndexName)
    .WithEnvironment("AZURE_SEARCH_API_KEY", azureSearchApiKey);

if (frontendMode == "vite-dev")
{
    var frontendDev = builder.AddNpmApp("frontend", "../frontend", "dev:aspire")
        .WaitFor(backend)
        .WaitFor(redis)
        .WithOtlpExporter()
        .WithHttpEndpoint(name: "http", port: 3000, env: "PORT")
        .WithDaprSidecar(new CommunityToolkit.Aspire.Hosting.Dapr.DaprSidecarOptions
        {
            AppId = "aihub-frontend",
            AppPort = 3000,
            ResourcesPaths = ImmutableHashSet.Create(daprComponentsPath)
        })
        .WithEnvironment("BACKEND_API_BASE_URL", "http://localhost:8080")
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
            AppId = "aihub-frontend",
            AppPort = 3000,
            ResourcesPaths = ImmutableHashSet.Create(daprComponentsPath)
        })
        .WithEnvironment("BACKEND_API_BASE_URL", "http://backend:8080")
        .WithEnvironment("BACKEND_DAPR_BASE_URL", "http://localhost:3500/v1.0/invoke/aihub-backend/method")
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
            Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
        }
    }
}
