using Aspire.Hosting.Dapr;
using CommunityToolkit.Aspire.Hosting.Dapr;

var builder = DistributedApplication.CreateBuilder(args);

// Add existing projects with Dapr support
var backend = builder.AddProject<Projects.Backend>("backend")
    .WithDaprSidecar(new CommunityToolkit.Aspire.Hosting.Dapr.DaprSidecarOptions { AppId = "aihub-backend", AppPort = 8080 });

var worker = builder.AddProject<Projects.Worker>("worker")
    .WithDaprSidecar(new CommunityToolkit.Aspire.Hosting.Dapr.DaprSidecarOptions { AppId = "aihub-worker", AppPort = 8081 });

var frontend = builder.AddNpmApp("frontend", "../frontend", "start")
    .WithDaprSidecar(new CommunityToolkit.Aspire.Hosting.Dapr.DaprSidecarOptions { AppId = "aihub-frontend", AppPort = 3000 });

builder.Build().Run();
