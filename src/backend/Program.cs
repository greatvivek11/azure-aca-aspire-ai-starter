using System.Diagnostics;
using AIHub.Backend.Features.AiPing;
using AIHub.Backend.Features.Chat;
using AIHub.Backend.Features.Customers;
using AIHub.Backend.Features.DocumentIngestion;
using AIHub.Backend.Features.Health;
using AIHub.Backend.Infrastructure.Ai;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Dapr.AspNetCore;
using Microsoft.Data.SqlClient;
using OpenTelemetry;

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
        .AddSource("AIHub.Backend"))
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

var azureOpenAiSettings = ResolveAzureOpenAiSettings(builder.Configuration);
var openAiAuthMode = (Environment.GetEnvironmentVariable("AZURE_OPENAI_AUTH_MODE") ?? "api-key").Trim().ToLowerInvariant();
var managedIdentityClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
builder.Services.AddSingleton<IAiService>(_ =>
    new FoundryChatService(
        _.GetRequiredService<IHttpClientFactory>(),
        azureOpenAiSettings,
        openAiAuthMode,
        managedIdentityClientId));

var app = builder.Build();

var sqlConnectionString = GetSqlConnectionString(app.Configuration);
await EnsureSqlSchemaAsync(sqlConnectionString);

var workerDaprBaseUrl = Environment.GetEnvironmentVariable("WORKER_DAPR_BASE_URL")
    ?? "http://localhost:3500/v1.0/invoke/aihub-worker/method";
var storageConnectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING") ?? string.Empty;
var storageContainerName = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONTAINER_NAME") ?? string.Empty;
var searchEndpoint = Environment.GetEnvironmentVariable("AZURE_SEARCH_ENDPOINT") ?? string.Empty;
var searchIndexName = Environment.GetEnvironmentVariable("AZURE_SEARCH_INDEX_NAME") ?? string.Empty;
var searchApiKey = Environment.GetEnvironmentVariable("AZURE_SEARCH_API_KEY");
var useManagedIdentityForSearch = string.Equals(
    Environment.GetEnvironmentVariable("AZURE_SEARCH_AUTH_MODE"),
    "managed-identity",
    StringComparison.OrdinalIgnoreCase);
var storageAccountName = Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_NAME") ?? string.Empty;
var storageAuthMode = (Environment.GetEnvironmentVariable("AZURE_STORAGE_AUTH_MODE") ?? "managed-identity")
    .Trim()
    .ToLowerInvariant();
var embeddingModelId = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_MODEL_ID")
    ?? azureOpenAiSettings.ModelId;
var embeddingDimensions = int.TryParse(Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DIMENSIONS"), out var parsedDimensions)
    ? parsedDimensions
    : 1536;

var uploadUrlExpiryMinutes = int.TryParse(Environment.GetEnvironmentVariable("UPLOAD_URL_EXPIRY_MINUTES"), out var parsedUploadExpiry)
    ? Math.Clamp(parsedUploadExpiry, 5, 120)
    : 15;

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.Use(async (context, next) =>
{
    var started = Stopwatch.GetTimestamp();
    try
    {
        await next();
        app.Logger.LogInformation(
            "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds} ms",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(
            ex,
            "HTTP {Method} {Path} failed after {ElapsedMilliseconds} ms",
            context.Request.Method,
            context.Request.Path,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        throw;
    }
});

app.UseCloudEvents();
app.MapSubscribeHandler();

app.MapHealthEndpoint();
app.MapAiPingEndpoint();
app.MapCustomerEndpoints(sqlConnectionString);
app.MapDocumentIngestionEndpoints(new DocumentIngestionOptions(
    sqlConnectionString,
    workerDaprBaseUrl,
    storageAccountName,
    storageConnectionString,
    storageContainerName,
    storageAuthMode,
    managedIdentityClientId,
    TimeSpan.FromMinutes(uploadUrlExpiryMinutes)));
app.MapChatEndpoint(new ChatOptions(
    azureOpenAiSettings,
    openAiAuthMode,
    embeddingModelId,
    embeddingDimensions,
    searchEndpoint,
    searchIndexName,
    searchApiKey,
    managedIdentityClientId,
    useManagedIdentityForSearch));

app.MapGet("/", () => "AI Hub Backend is running!");

app.Run();

static async Task EnsureSqlSchemaAsync(string connectionString)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    var seedScriptPath = Path.Combine(AppContext.BaseDirectory, "Infrastructure", "Sql", "seed.sql");
    var seedScript = await File.ReadAllTextAsync(seedScriptPath);

    await using var seedCommand = connection.CreateCommand();
    seedCommand.CommandText = seedScript;
    await seedCommand.ExecuteNonQueryAsync();
}

static AzureOpenAiRuntimeSettings ResolveAzureOpenAiSettings(IConfiguration configuration)
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

static string GetSqlConnectionString(IConfiguration configuration)
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
