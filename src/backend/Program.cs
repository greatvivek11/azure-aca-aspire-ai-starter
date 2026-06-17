using AcaAspireAiTemplate.Backend.Features.AiPing;
using AcaAspireAiTemplate.Backend.Features.Chat;
using AcaAspireAiTemplate.Backend.Features.Customers;
using AcaAspireAiTemplate.Backend.Features.DocumentIngestion;
using AcaAspireAiTemplate.Backend.Features.Health;
using AcaAspireAiTemplate.Backend.Infrastructure.Ai;
using AcaAspireAiTemplate.Backend.Infrastructure.Auth;
using AcaAspireAiTemplate.Backend.Infrastructure.Startup;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Dapr.AspNetCore;
using OpenTelemetry;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprClient();
builder.Services.AddHealthChecks();
builder.Services.AddHttpClient();
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

var otelBuilder = builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Microsoft.AspNetCore")
        .AddSource("AcaAspireAiTemplate.Backend"))
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

builder.Services.Configure<AzureOpenAiOptions>(
    builder.Configuration.GetSection(AzureOpenAiOptions.SectionName));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var identity = context.User?.Identity?.Name;
        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        var partitionKey = string.IsNullOrWhiteSpace(identity)
            ? (string.IsNullOrWhiteSpace(remoteIp) ? "anonymous" : $"ip:{remoteIp}")
            : $"user:{identity}";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

var entraAuthOptions = EntraAuthSetup.ResolveEntraAuthOptions(builder.Configuration);
builder.Services.AddEntraAuth(entraAuthOptions);

var runtimeOptions = BackendRuntimeOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton<IDocumentIngestionStore>(_ => new SqlDocumentIngestionStore(runtimeOptions.SqlConnectionString));
builder.Services.AddSingleton<IAiService>(_ => runtimeOptions.AiMode == "local"
    ? new OllamaChatService(
        _.GetRequiredService<IHttpClientFactory>(),
        runtimeOptions.OllamaBaseUrl,
        runtimeOptions.OllamaChatModel)
    : new FoundryChatService(
        _.GetRequiredService<IHttpClientFactory>(),
        runtimeOptions.AzureOpenAiSettings!,
        runtimeOptions.OpenAiAuthMode,
        runtimeOptions.ManagedIdentityClientId));

var app = builder.Build();

var sqlConnectionString = runtimeOptions.SqlConnectionString;
var skipStartupTasksForTests =
    string.Equals(
        Environment.GetEnvironmentVariable("BACKEND_SKIP_STARTUP_TASKS_FOR_TESTS"),
        "true",
        StringComparison.OrdinalIgnoreCase);

if (skipStartupTasksForTests)
{
    app.Logger.LogWarning(
        "BACKEND_SKIP_STARTUP_TASKS_FOR_TESTS=true. SQL/blob startup tasks are skipped for this host instance.");
}
else
{
    if (string.Equals(runtimeOptions.AiMode, "local", StringComparison.OrdinalIgnoreCase))
    {
        await BackendStartupTasks.RunStartupStepAsync(
            "Ensuring SQL database",
            () => BackendStartupTasks.EnsureDatabaseExistsAsync(sqlConnectionString),
            app.Logger);
    }
    else
    {
        app.Logger.LogInformation("Skipping SQL database creation check in Azure mode.");
    }

    await BackendStartupTasks.RunStartupStepAsync(
        "Ensuring SQL schema",
        () => BackendStartupTasks.EnsureSqlSchemaAsync(sqlConnectionString),
        app.Logger);

    await BackendStartupTasks.RunStartupStepAsync(
        "Ensuring local blob storage",
        () => BackendStartupTasks.EnsureLocalBlobStorageAsync(runtimeOptions.StorageConnectionString, runtimeOptions.StorageContainerName, app.Logger),
        app.Logger);
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseBackendRequestLogging();

app.UseCloudEvents();
app.MapSubscribeHandler().AllowAnonymous();

if (entraAuthOptions.Enabled)
{
    app.UseAuthentication();
}

app.UseAuthorization();
app.UseRateLimiter();
app.UseUploadRequestProtection(runtimeOptions.UploadMaxRequestBytes);

app.MapHealthEndpoint();
app.MapAiPingEndpoint();
app.MapCustomerEndpoints(sqlConnectionString);
app.MapDocumentIngestionEndpoints(new DocumentIngestionOptions(
    sqlConnectionString,
    runtimeOptions.WorkerDaprBaseUrl,
    runtimeOptions.StorageAccountName,
    runtimeOptions.StorageConnectionString,
    runtimeOptions.StorageContainerName,
    runtimeOptions.StorageAuthMode,
    runtimeOptions.StoragePublicBlobEndpoint,
    runtimeOptions.ManagedIdentityClientId,
    TimeSpan.FromMinutes(runtimeOptions.UploadUrlExpiryMinutes)));
app.MapChatEndpoint(new ChatOptions(
    runtimeOptions.AiMode,
    runtimeOptions.AzureOpenAiSettings,
    runtimeOptions.OpenAiAuthMode,
    runtimeOptions.EmbeddingModelId,
    runtimeOptions.EmbeddingDimensions,
    runtimeOptions.OllamaBaseUrl,
    runtimeOptions.QdrantUrl,
    runtimeOptions.QdrantCollection,
    runtimeOptions.SearchEndpoint,
    runtimeOptions.SearchIndexName,
    runtimeOptions.SearchApiKey,
    runtimeOptions.ManagedIdentityClientId,
    runtimeOptions.UseManagedIdentityForSearch));

app.MapGet("/", () => "ACA Aspire AI Starter Backend is running!").AllowAnonymous();

if (!skipStartupTasksForTests)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await BackendStartupTasks.WarmLocalOllamaModelsAsync(
                    runtimeOptions.AiMode,
                    app.Services.GetRequiredService<IHttpClientFactory>(),
                    runtimeOptions.OllamaBaseUrl,
                    [runtimeOptions.OllamaChatModel, runtimeOptions.OllamaEmbedModel],
                    app.Logger);
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Background Ollama model warmup failed.");
            }
        });
    });
}

app.Run();

public partial class Program;

