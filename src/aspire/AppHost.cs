using Aspire.Hosting.Dapr;
using CommunityToolkit.Aspire.Hosting.Dapr;
using System;
using System.IO;

var builder = DistributedApplication.CreateBuilder(args);

// Enable Docker publisher
builder.AddDockerComposeEnvironment("aspire-docker-demo");

// Load environment variables from .env file
LoadEnvFile(".env");

// Centralized configuration management
var huggingFaceApiKey = builder.AddParameter("huggingFaceApiKey", secret: true);
var huggingFaceModelId = builder.AddParameter("huggingFaceModelId");
var huggingFaceEndpoint = builder.AddParameter("huggingFaceEndpoint");

// Parameter values will be read from appsettings.json or environment variables
// You can also set them through the .NET Aspire dashboard when running the app

// Add existing projects with Dapr support and Dockerfiles
var backend = builder.AddDockerfile("backend", "../backend")
    .WithDaprSidecar(new CommunityToolkit.Aspire.Hosting.Dapr.DaprSidecarOptions { AppId = "aihub-backend", AppPort = 80 })
    .WithEnvironment("HUGGINGFACE_API_KEY", huggingFaceApiKey)
    .WithEnvironment("HUGGINGFACE_MODEL_ID", huggingFaceModelId)
    .WithEnvironment("HUGGINGFACE_ENDPOINT", huggingFaceEndpoint);

var worker = builder.AddDockerfile("worker", "../worker")
    .WithDaprSidecar(new CommunityToolkit.Aspire.Hosting.Dapr.DaprSidecarOptions { AppId = "aihub-worker", AppPort = 8081 })
    .WithEnvironment("HUGGINGFACE_API_KEY", huggingFaceApiKey)
    .WithEnvironment("HUGGINGFACE_MODEL_ID", huggingFaceModelId)
    .WithEnvironment("HUGGINGFACE_ENDPOINT", huggingFaceEndpoint);

var frontend = builder.AddDockerfile("frontend", "../frontend")
    .WithDaprSidecar(new CommunityToolkit.Aspire.Hosting.Dapr.DaprSidecarOptions { AppId = "aihub-frontend", AppPort = 3000 });

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
