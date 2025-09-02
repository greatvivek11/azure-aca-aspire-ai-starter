using Aspire.Hosting.Dapr;
using CommunityToolkit.Aspire.Hosting.Dapr;
using System;
using System.IO;

var builder = DistributedApplication.CreateBuilder(args);

// Load environment variables from .env file
LoadEnvFile(".env");

// Centralized configuration management
var huggingFaceApiKey = builder.AddParameter("huggingFaceApiKey", secret: true);
var huggingFaceModelId = builder.AddParameter("huggingFaceModelId");
var huggingFaceEndpoint = builder.AddParameter("huggingFaceEndpoint");

// Set parameter values from environment variables
huggingFaceApiKey.Resource.SecretValue = Environment.GetEnvironmentVariable("HUGGINGFACE_API_KEY") ?? "";
huggingFaceModelId.Resource.Value = Environment.GetEnvironmentVariable("HUGGINGFACE_MODEL_ID") ?? "gpt2";
huggingFaceEndpoint.Resource.Value = Environment.GetEnvironmentVariable("HUGGINGFACE_ENDPOINT") ?? "https://api-inference.huggingface.co/v1/";

// Add existing projects with Dapr support
var backend = builder.AddProject<Projects.Backend>("backend")
    .WithDaprSidecar(new CommunityToolkit.Aspire.Hosting.Dapr.DaprSidecarOptions { AppId = "aihub-backend", AppPort = 8080 })
    .WithEnvironment("HUGGINGFACE_API_KEY", huggingFaceApiKey)
    .WithEnvironment("HUGGINGFACE_MODEL_ID", huggingFaceModelId)
    .WithEnvironment("HUGGINGFACE_ENDPOINT", huggingFaceEndpoint);

var worker = builder.AddProject<Projects.Worker>("worker")
    .WithDaprSidecar(new CommunityToolkit.Aspire.Hosting.Dapr.DaprSidecarOptions { AppId = "aihub-worker", AppPort = 8081 })
    .WithEnvironment("HUGGINGFACE_API_KEY", huggingFaceApiKey)
    .WithEnvironment("HUGGINGFACE_MODEL_ID", huggingFaceModelId)
    .WithEnvironment("HUGGINGFACE_ENDPOINT", huggingFaceEndpoint);

var frontend = builder.AddNpmApp("frontend", "../frontend", "start")
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
