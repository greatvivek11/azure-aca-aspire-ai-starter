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
    .WithDaprSidecar(new CommunityToolkit.Aspire.Hosting.Dapr.DaprSidecarOptions
    {
        AppId = "aihub-backend",
        AppPort = 8080,
        ResourcesPaths = ImmutableHashSet.Create(daprComponentsPath)
    })
    .WithEnvironment("AZURE_OPENAI_API_KEY", azureOpenAiApiKey)
    .WithEnvironment("AZURE_OPENAI_MODEL_ID", azureOpenAiModelId)
    .WithEnvironment("AZURE_OPENAI_ENDPOINT", azureOpenAiEndpoint)
    .WithEnvironment("ConnectionStrings__SqlServer", "Server=sql,1433;Database=AIHub;User Id=sa;Password=P@ssw0rd;TrustServerCertificate=true")
    .WithEnvironment("ConnectionStrings__Redis", "redis:6379")
    .WithEnvironment("REDIS_CONNECTION_STRING", "redis:6379");

var worker = builder.AddDockerfile("worker", "../worker")
    .WaitFor(sql)
    .WaitFor(redis)
    .WithOtlpExporter()
    .WithDaprSidecar(new CommunityToolkit.Aspire.Hosting.Dapr.DaprSidecarOptions
    {
        AppId = "aihub-worker",
        AppPort = 8081,
        ResourcesPaths = ImmutableHashSet.Create(daprComponentsPath)
    })
    .WithEnvironment("ConnectionStrings__SqlServer", "Server=sql,1433;Database=AIHub;User Id=sa;Password=P@ssw0rd;TrustServerCertificate=true")
    .WithEnvironment("ConnectionStrings__Redis", "redis:6379")
    .WithEnvironment("REDIS_CONNECTION_STRING", "redis:6379");

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
