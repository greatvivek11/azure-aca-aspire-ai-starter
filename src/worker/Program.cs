using Azure.Monitor.OpenTelemetry.AspNetCore;
using Dapr.AspNetCore;
using OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);
var runtimeOptions = WorkerRuntimeOptions.FromEnvironment(builder.Configuration);

builder.Services.AddDaprClient();
builder.Services.AddHealthChecks();
builder.Services.AddHttpClient();
builder.Services.AddSingleton(runtimeOptions);
builder.Services.AddSingleton<AzureAuthenticator>();
builder.Services.AddSingleton<IDocumentJobRepository, DocumentJobRepository>();
builder.Services.AddSingleton<BlobStorageClient>();
builder.Services.AddSingleton<TextExtractor>();
builder.Services.AddSingleton<TextChunker>();
builder.Services.AddSingleton<AzureOpenAiEmbeddingService>();
builder.Services.AddSingleton<LlamaCppEmbeddingService>();
builder.Services.AddSingleton<AzureSearchIndexer>();
builder.Services.AddSingleton<QdrantIndexer>();
builder.Services.AddSingleton<DocumentProcessor>();
builder.Services.AddSingleton<IngestionQueueProcessor>();
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

var otelBuilder = builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Microsoft.AspNetCore")
        .AddSource("AcaAspireAiTemplate.Worker"))
    .WithMetrics(metrics => metrics
        .AddMeter("Microsoft.AspNetCore.Hosting")
        .AddMeter("Microsoft.AspNetCore.Server.Kestrel"));
otelBuilder.UseOtlpExporter();

var appInsightsConnectionString =
    builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
    ?? builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    otelBuilder.UseAzureMonitor();
}

builder.WebHost.UseUrls("http://*:8081");

var app = builder.Build();

if (string.Equals(runtimeOptions.AiMode, "local", StringComparison.OrdinalIgnoreCase))
{
    await WorkerStartupTasks.EnsureDatabaseExistsAsync(runtimeOptions.SqlConnectionString);
}
else
{
    app.Logger.LogInformation("Skipping SQL database creation check in Azure mode.");
}

app.Logger.LogInformation(
    "Worker startup configuration resolved. AiMode={AiMode}, IngestionConfigured={IngestionConfigured}, StorageConfigured={StorageConfigured}, QdrantUrlConfigured={QdrantConfigured}, SearchConfigured={SearchConfigured}, OpenAiEndpointConfigured={OpenAiEndpointConfigured}",
    runtimeOptions.AiMode,
    runtimeOptions.IngestionConfigured,
    runtimeOptions.StorageConfigured,
    !string.IsNullOrWhiteSpace(runtimeOptions.QdrantUrl),
    !string.IsNullOrWhiteSpace(runtimeOptions.SearchEndpoint) && !string.IsNullOrWhiteSpace(runtimeOptions.SearchIndexName),
    !string.IsNullOrWhiteSpace(runtimeOptions.OpenAiEndpointText));

if (string.Equals(runtimeOptions.AiMode, "local", StringComparison.OrdinalIgnoreCase))
{
    app.Logger.LogInformation("Local AI startup warmup is disabled; llama.cpp model availability is managed by local server startup configuration.");
}

if (runtimeOptions.IngestionConfigured)
{
    if (string.Equals(runtimeOptions.AiMode, "local", StringComparison.OrdinalIgnoreCase))
    {
        app.Logger.LogInformation(
            "Ensuring Qdrant collection. Url={QdrantUrl}, Collection={Collection}, Dimensions={Dimensions}",
            runtimeOptions.QdrantUrl,
            runtimeOptions.QdrantCollection,
            runtimeOptions.EmbeddingDimensions);
        await app.Services.GetRequiredService<QdrantIndexer>().EnsureCollectionAsync(app.Logger, app.Lifetime.ApplicationStopping);
    }
    else
    {
        app.Logger.LogInformation(
            "Ensuring Azure AI Search index. Endpoint={SearchEndpoint}, Index={IndexName}, Dimensions={Dimensions}",
            runtimeOptions.SearchEndpoint,
            runtimeOptions.SearchIndexName,
            runtimeOptions.EmbeddingDimensions);
        await app.Services.GetRequiredService<AzureSearchIndexer>().EnsureIndexAsync(app.Lifetime.ApplicationStopping);
    }

    await WorkerStartupTasks.EnsureWorkerSqlSchemaAsync(runtimeOptions.SqlConnectionString);
    await WorkerStartupTasks.RequeueNonTerminalJobsAsync(runtimeOptions.SqlConnectionString);
    _ = app.Services.GetRequiredService<IngestionQueueProcessor>()
        .RunAsync(app.Lifetime.ApplicationStopping);
}
else
{
    app.Logger.LogWarning(
        "Worker ingestion pipeline disabled. AiMode={AiMode}, StorageConfigured={StorageConfigured}, LocalIngestionConfigured={LocalConfigured}, AzureIngestionConfigured={AzureConfigured}",
        runtimeOptions.AiMode,
        runtimeOptions.StorageConfigured,
        runtimeOptions.LocalIngestionConfigured,
        runtimeOptions.AzureIngestionConfigured);
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseWorkerRequestLogging();

app.UseCloudEvents();
app.MapSubscribeHandler();
app.MapHealthChecks("/v1/health");

app.MapPost("/v1/ingest", async (WorkerIngestRequest request, WorkerRuntimeOptions options, IDocumentJobRepository jobRepository) =>
{
    if (!options.IngestionConfigured)
    {
        return Results.Problem("Worker ingestion pipeline is not configured.");
    }

    var job = await jobRepository.GetAsync(request.DocumentId);
    if (job is null)
    {
        return Results.NotFound($"Document {request.DocumentId} was not found.");
    }

    await jobRepository.UpdateStatusAsync(request.DocumentId, "Queued", 15, null);

    return Results.Accepted($"/v1/ingest/{request.DocumentId}", new { request.DocumentId, status = "Queued" });
});

app.MapGet("/", () => "ACA Aspire AI Starter Worker is running!");
app.Run();
