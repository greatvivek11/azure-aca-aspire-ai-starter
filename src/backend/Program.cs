using Azure.Storage.Blobs;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AcaAspireAiTemplate.Backend.Features.AiPing;
using AcaAspireAiTemplate.Backend.Features.Chat;
using AcaAspireAiTemplate.Backend.Features.Customers;
using AcaAspireAiTemplate.Backend.Features.DocumentIngestion;
using AcaAspireAiTemplate.Backend.Features.Health;
using AcaAspireAiTemplate.Backend.Infrastructure.Ai;
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

var aiMode = (Environment.GetEnvironmentVariable("AI_MODE") ?? "azure").Trim().ToLowerInvariant();
var ollamaBaseUrl = (Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://ollama:11434").Trim();
var ollamaChatModel = (Environment.GetEnvironmentVariable("OLLAMA_CHAT_MODEL") ?? "gemma3:4b-it-qat").Trim();
var ollamaEmbedModel = (Environment.GetEnvironmentVariable("OLLAMA_EMBED_MODEL") ?? "nomic-embed-text").Trim();
var qdrantUrl = (Environment.GetEnvironmentVariable("QDRANT_URL") ?? "http://qdrant:6333").Trim();
var qdrantCollection = (Environment.GetEnvironmentVariable("QDRANT_COLLECTION") ?? "documents").Trim();

var azureOpenAiSettings = aiMode == "azure"
    ? ResolveAzureOpenAiSettings(builder.Configuration)
    : null;
var openAiAuthMode = (Environment.GetEnvironmentVariable("AZURE_OPENAI_AUTH_MODE") ?? "api-key").Trim().ToLowerInvariant();
var managedIdentityClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
builder.Services.AddSingleton<IAiService>(_ => aiMode == "local"
    ? new OllamaChatService(
        _.GetRequiredService<IHttpClientFactory>(),
        ollamaBaseUrl,
        ollamaChatModel)
    : new FoundryChatService(
        _.GetRequiredService<IHttpClientFactory>(),
        azureOpenAiSettings!,
        openAiAuthMode,
        managedIdentityClientId));

var app = builder.Build();

var sqlConnectionString = GetSqlConnectionString(app.Configuration);
if (string.Equals(aiMode, "local", StringComparison.OrdinalIgnoreCase))
{
    await RunStartupStepAsync("Ensuring SQL database", () => EnsureDatabaseExistsAsync(sqlConnectionString), app.Logger);
}
else
{
    app.Logger.LogInformation("Skipping SQL database creation check in Azure mode.");
}
await RunStartupStepAsync("Ensuring SQL schema", () => EnsureSqlSchemaAsync(sqlConnectionString), app.Logger);

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
var storageAccountName = Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_NAME") ?? string.Empty;
var storageAuthMode = (Environment.GetEnvironmentVariable("AZURE_STORAGE_AUTH_MODE") ?? "managed-identity")
    .Trim()
    .ToLowerInvariant();
var embeddingModelId = (aiMode == "local"
    ? Environment.GetEnvironmentVariable("OLLAMA_EMBED_MODEL") ?? "nomic-embed-text"
    : Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_MODEL_ID") ?? string.Empty).Trim();
if (string.IsNullOrWhiteSpace(embeddingModelId) && aiMode == "azure")
{
    throw new InvalidOperationException("AZURE_OPENAI_EMBEDDING_MODEL_ID is required for document grounding and must reference an embeddings deployment.");
}
var embeddingDimensions = aiMode == "local"
    ? (int.TryParse(Environment.GetEnvironmentVariable("OLLAMA_EMBED_DIMENSIONS"), out var parsedLocalDimensions)
        ? parsedLocalDimensions
        : 768)
    : (int.TryParse(Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DIMENSIONS"), out var parsedDimensions)
        ? parsedDimensions
        : 1536);

var uploadUrlExpiryMinutes = int.TryParse(Environment.GetEnvironmentVariable("UPLOAD_URL_EXPIRY_MINUTES"), out var parsedUploadExpiry)
    ? Math.Clamp(parsedUploadExpiry, 5, 120)
    : 15;
await RunStartupStepAsync(
    "Ensuring local blob storage",
    () => EnsureLocalBlobStorageAsync(storageConnectionString, storageContainerName, app.Logger),
    app.Logger);

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
    storagePublicBlobEndpoint,
    managedIdentityClientId,
    TimeSpan.FromMinutes(uploadUrlExpiryMinutes)));
app.MapChatEndpoint(new ChatOptions(
    aiMode,
    azureOpenAiSettings,
    openAiAuthMode,
    embeddingModelId,
    embeddingDimensions,
    ollamaBaseUrl,
    qdrantUrl,
    qdrantCollection,
    searchEndpoint,
    searchIndexName,
    searchApiKey,
    managedIdentityClientId,
    useManagedIdentityForSearch));

app.MapGet("/", () => "ACA Aspire AI Starter Backend is running!");

app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        try
        {
            await WarmLocalOllamaModelsAsync(
                aiMode,
                app.Services.GetRequiredService<IHttpClientFactory>(),
                ollamaBaseUrl,
                [ollamaChatModel, ollamaEmbedModel],
                app.Logger);
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Background Ollama model warmup failed.");
        }
    });
});

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

static async Task EnsureDatabaseExistsAsync(string connectionString)
{
    var builder = new SqlConnectionStringBuilder(connectionString);
    var databaseName = builder.InitialCatalog;
    if (string.IsNullOrWhiteSpace(databaseName))
    {
        return;
    }

    builder.InitialCatalog = "master";
    await using var connection = new SqlConnection(builder.ConnectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = """
IF DB_ID(@databaseName) IS NULL
BEGIN
    DECLARE @sql nvarchar(max) = N'CREATE DATABASE [' + REPLACE(@databaseName, ']', ']]') + N']';
    EXEC (@sql);
END
""";
    command.Parameters.AddWithValue("@databaseName", databaseName);
    await command.ExecuteNonQueryAsync();
}

static async Task EnsureLocalBlobStorageAsync(
    string storageConnectionString,
    string storageContainerName,
    ILogger logger)
{
    if (string.IsNullOrWhiteSpace(storageConnectionString) || string.IsNullOrWhiteSpace(storageContainerName))
    {
        logger.LogInformation("Skipping local blob storage initialization because storage connection settings are incomplete.");
        return;
    }

    if (!storageConnectionString.Contains("AccountName=devstoreaccount1", StringComparison.OrdinalIgnoreCase))
    {
        logger.LogInformation("Skipping local blob storage initialization because the configured storage account is not the local Azurite account.");
        return;
    }

    var blobServiceClient = new BlobServiceClient(storageConnectionString);
    var containerClient = blobServiceClient.GetBlobContainerClient(storageContainerName);
    await containerClient.CreateIfNotExistsAsync();
    logger.LogInformation("Local blob storage container is ready. Container={ContainerName}", storageContainerName);
}

static async Task WarmLocalOllamaModelsAsync(
    string aiMode,
    IHttpClientFactory httpClientFactory,
    string ollamaBaseUrl,
    IReadOnlyList<string> modelNames,
    ILogger logger)
{
    if (!string.Equals(aiMode, "local", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    var uniqueModelNames = modelNames
        .Where(modelName => !string.IsNullOrWhiteSpace(modelName))
        .Select(modelName => modelName.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (uniqueModelNames.Length == 0)
    {
        logger.LogWarning("Skipping Ollama model warmup because no local model names were configured.");
        return;
    }

    using var client = httpClientFactory.CreateClient();
    foreach (var modelName in uniqueModelNames)
    {
        logger.LogInformation("Preloading Ollama model {ModelName}.", modelName);
        using var payload = new StringContent(
            JsonSerializer.Serialize(new { name = modelName, stream = false }),
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync($"{ollamaBaseUrl.TrimEnd('/')}/api/pull", payload);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            logger.LogWarning(
                "Skipping Ollama model warmup for {ModelName}. StatusCode={StatusCode}, Response={ResponseBody}",
                modelName,
                (int)response.StatusCode,
                string.IsNullOrWhiteSpace(responseBody) ? "<empty>" : responseBody);
            continue;
        }

        logger.LogInformation("Ollama model {ModelName} is ready.", modelName);
    }
}

static async Task RunStartupStepAsync(string stepName, Func<Task> action, ILogger logger)
{
    logger.LogInformation("{StartupStep} started.", stepName);

    try
    {
        await action();
        logger.LogInformation("{StartupStep} completed.", stepName);
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "{StartupStep} failed.", stepName);
        throw;
    }
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
