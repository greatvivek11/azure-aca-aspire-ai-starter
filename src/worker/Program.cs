using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Azure.Storage.Blobs;
using Dapr.AspNetCore;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Data.SqlClient;
using OpenTelemetry;
using UglyToad.PdfPig;

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
        .AddSource("AIHub.Worker"))
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

var sqlConnectionString = GetSqlConnectionString(app.Configuration);
var storageConnectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING") ?? string.Empty;
var storageContainerName = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONTAINER_NAME") ?? string.Empty;
var searchEndpoint = Environment.GetEnvironmentVariable("AZURE_SEARCH_ENDPOINT") ?? string.Empty;
var searchIndexName = Environment.GetEnvironmentVariable("AZURE_SEARCH_INDEX_NAME") ?? string.Empty;
var searchApiKey = Environment.GetEnvironmentVariable("AZURE_SEARCH_API_KEY") ?? string.Empty;
var openAiEndpointText = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? string.Empty;
var openAiApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ?? string.Empty;
var embeddingModelId = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_MODEL_ID") ?? string.Empty;
var embeddingDimensions = int.TryParse(Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DIMENSIONS"), out var parsed)
    ? parsed
    : 1536;
var ingestionConfigured =
    !string.IsNullOrWhiteSpace(storageConnectionString) &&
    !string.IsNullOrWhiteSpace(storageContainerName) &&
    !string.IsNullOrWhiteSpace(searchEndpoint) &&
    !string.IsNullOrWhiteSpace(searchIndexName) &&
    !string.IsNullOrWhiteSpace(searchApiKey) &&
    !string.IsNullOrWhiteSpace(openAiEndpointText) &&
    !string.IsNullOrWhiteSpace(openAiApiKey) &&
    !string.IsNullOrWhiteSpace(embeddingModelId);

if (ingestionConfigured)
{
    await EnsureSearchIndexAsync(searchEndpoint, searchApiKey, searchIndexName, embeddingDimensions);
    await RequeueNonTerminalJobsAsync(sqlConnectionString);
    _ = RunIngestionLoopAsync(
        sqlConnectionString,
        storageConnectionString,
        storageContainerName,
        searchEndpoint,
        searchApiKey,
        searchIndexName,
        new Uri(openAiEndpointText, UriKind.Absolute),
        openAiApiKey,
        embeddingModelId,
        httpClientFactory: app.Services.GetRequiredService<IHttpClientFactory>(),
        app.Logger,
        app.Lifetime.ApplicationStopping);
}

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
app.MapHealthChecks("/v1/health");

app.MapPost("/v1/ingest", async (WorkerIngestRequest request) =>
{
    if (!ingestionConfigured)
    {
        return Results.Problem("Worker ingestion pipeline is not configured.");
    }

    var job = await GetDocumentIngestionJobAsync(sqlConnectionString, request.DocumentId);
    if (job is null)
    {
        return Results.NotFound($"Document {request.DocumentId} was not found.");
    }

    await UpdateDocumentIngestionJobStatusAsync(sqlConnectionString, request.DocumentId, "Queued", 15, null);

    return Results.Accepted($"/v1/ingest/{request.DocumentId}", new { request.DocumentId, status = "Queued" });
});

app.MapGet("/", () => "AI Hub Worker is running!");
app.Run();

static async Task RunIngestionLoopAsync(
    string sqlConnectionString,
    string storageConnectionString,
    string storageContainerName,
    string searchEndpoint,
    string searchApiKey,
    string searchIndexName,
    Uri openAiEndpoint,
    string openAiApiKey,
    string embeddingModelId,
    IHttpClientFactory httpClientFactory,
    ILogger logger,
    CancellationToken cancellationToken)
{
    logger.LogInformation("Starting ingestion queue processor.");

    while (!cancellationToken.IsCancellationRequested)
    {
        var claimedJob = await TryClaimNextQueuedJobAsync(sqlConnectionString);
        if (claimedJob is null)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            continue;
        }

        try
        {
            await ProcessDocumentAsync(
                claimedJob.DocumentId,
                sqlConnectionString,
                storageConnectionString,
                storageContainerName,
                searchEndpoint,
                searchApiKey,
                searchIndexName,
                openAiEndpoint,
                openAiApiKey,
                embeddingModelId,
                httpClientFactory,
                logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while processing document {DocumentId}", claimedJob.DocumentId);
            await UpdateDocumentIngestionJobStatusAsync(
                sqlConnectionString,
                claimedJob.DocumentId,
                "Failed",
                100,
                ex.Message);
        }
    }
}

static async Task ProcessDocumentAsync(
    Guid documentId,
    string sqlConnectionString,
    string storageConnectionString,
    string storageContainerName,
    string searchEndpoint,
    string searchApiKey,
    string searchIndexName,
    Uri openAiEndpoint,
    string openAiApiKey,
    string embeddingModelId,
    IHttpClientFactory httpClientFactory,
    ILogger logger)
{
    var job = await GetDocumentIngestionJobAsync(sqlConnectionString, documentId);
    if (job is null)
    {
        throw new InvalidOperationException($"Document {documentId} does not exist.");
    }

    await UpdateDocumentIngestionJobStatusAsync(sqlConnectionString, documentId, "Extracting", 25, null);

    var blobServiceClient = new BlobServiceClient(storageConnectionString);
    var containerClient = blobServiceClient.GetBlobContainerClient(storageContainerName);
    var blobClient = containerClient.GetBlobClient(job.BlobName);

    if (!await blobClient.ExistsAsync())
    {
        throw new InvalidOperationException($"Blob '{job.BlobName}' was not found.");
    }

    await using var blobStream = await blobClient.OpenReadAsync();
    var extractedText = await ExtractTextAsync(job.FileName, blobStream);
    if (string.IsNullOrWhiteSpace(extractedText))
    {
        throw new InvalidOperationException("No text content was extracted from the uploaded file.");
    }

    await UpdateDocumentIngestionJobStatusAsync(sqlConnectionString, documentId, "Chunking", 45, null);
    var chunks = ChunkText(extractedText, 900, 120);
    if (chunks.Count == 0)
    {
        throw new InvalidOperationException("Document text could not be chunked.");
    }

    await UpdateDocumentIngestionJobStatusAsync(sqlConnectionString, documentId, "Embedding", 65, null);
    var searchDocuments = new List<SearchChunkDocument>();
    for (var index = 0; index < chunks.Count; index++)
    {
        var chunk = chunks[index];
        var embedding = await GenerateEmbeddingAsync(
            chunk,
            openAiEndpoint,
            openAiApiKey,
            embeddingModelId,
            httpClientFactory);

        searchDocuments.Add(new SearchChunkDocument
        {
            Id = $"{documentId:N}-{index:D5}",
            DocumentId = documentId.ToString(),
            ChunkId = $"chunk-{index + 1}",
            FileName = job.FileName,
            Content = chunk,
            ContentVector = embedding
        });
    }

    await UpdateDocumentIngestionJobStatusAsync(sqlConnectionString, documentId, "Indexing", 85, null);

    var searchClient = new SearchClient(
        new Uri(searchEndpoint),
        searchIndexName,
        new AzureKeyCredential(searchApiKey));
    await searchClient.MergeOrUploadDocumentsAsync(searchDocuments);

    await UpdateDocumentIngestionJobStatusAsync(
        sqlConnectionString,
        documentId,
        "Ready",
        100,
        null,
        chunks.Count,
        true);

    logger.LogInformation(
        "Document {DocumentId} ingestion completed successfully. Chunks indexed: {ChunkCount}",
        documentId,
        chunks.Count);
}

static async Task EnsureSearchIndexAsync(
    string searchEndpoint,
    string searchApiKey,
    string searchIndexName,
    int embeddingDimensions)
{
    var indexClient = new SearchIndexClient(new Uri(searchEndpoint), new AzureKeyCredential(searchApiKey));

    try
    {
        await indexClient.GetIndexAsync(searchIndexName);
        return;
    }
    catch (RequestFailedException ex) when (ex.Status == 404)
    {
        // Continue and create the index.
    }

    var fields = new List<SearchField>
    {
        new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
        new SimpleField("documentId", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
        new SimpleField("chunkId", SearchFieldDataType.String) { IsFilterable = true },
        new SearchableField("fileName") { IsFilterable = true, IsSortable = true },
        new SearchableField("content"),
        new SearchField("contentVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
        {
            IsSearchable = true,
            VectorSearchDimensions = embeddingDimensions,
            VectorSearchProfileName = "vector-profile"
        }
    };

    var index = new SearchIndex(searchIndexName, fields)
    {
        VectorSearch = new VectorSearch
        {
            Algorithms = { new HnswAlgorithmConfiguration("hnsw-default") },
            Profiles = { new VectorSearchProfile("vector-profile", "hnsw-default") }
        }
    };

    await indexClient.CreateOrUpdateIndexAsync(index);
}

static async Task<float[]> GenerateEmbeddingAsync(
    string text,
    Uri endpoint,
    string apiKey,
    string embeddingDeployment,
    IHttpClientFactory httpClientFactory)
{
    using var client = httpClientFactory.CreateClient();
    client.DefaultRequestHeaders.Add("api-key", apiKey);

    var requestUri = BuildEmbeddingsRequestUri(endpoint, embeddingDeployment);
    using var payload = new StringContent(
        JsonSerializer.Serialize(new { input = text }),
        Encoding.UTF8,
        "application/json");
    using var response = await client.PostAsync(requestUri, payload);
    response.EnsureSuccessStatusCode();

    var json = await response.Content.ReadAsStringAsync();
    using var document = JsonDocument.Parse(json);
    return document.RootElement
        .GetProperty("data")[0]
        .GetProperty("embedding")
        .EnumerateArray()
        .Select(element => element.GetSingle())
        .ToArray();
}

static string BuildEmbeddingsRequestUri(Uri endpoint, string embeddingDeployment)
{
    var baseUri = NormalizeOpenAiEndpointBase(endpoint);
    return $"{baseUri}/openai/deployments/{embeddingDeployment}/embeddings?api-version=2024-10-21";
}

static string NormalizeOpenAiEndpointBase(Uri endpoint)
{
    var authority = endpoint.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    var normalizedPath = endpoint.AbsolutePath.TrimEnd('/');

    if (normalizedPath.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
    {
        normalizedPath = normalizedPath[..^"/openai/v1".Length];
    }
    else if (normalizedPath.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
    {
        normalizedPath = normalizedPath[..^"/openai".Length];
    }

    normalizedPath = normalizedPath.TrimEnd('/');
    return string.IsNullOrWhiteSpace(normalizedPath) ? authority : $"{authority}{normalizedPath}";
}

static async Task<string> ExtractTextAsync(string fileName, Stream blobStream)
{
    var extension = Path.GetExtension(fileName).ToLowerInvariant();
    return extension switch
    {
        ".txt" => await ReadTextFileAsync(blobStream),
        ".pdf" => await ReadPdfFileAsync(blobStream),
        ".docx" => await ReadDocxFileAsync(blobStream),
        _ => await ReadTextFileAsync(blobStream)
    };
}

static async Task<string> ReadTextFileAsync(Stream stream)
{
    stream.Position = 0;
    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
    return await reader.ReadToEndAsync();
}

static Task<string> ReadPdfFileAsync(Stream stream)
{
    stream.Position = 0;
    using var document = PdfDocument.Open(stream);
    var content = string.Join("\n", document.GetPages().Select(page => page.Text));
    return Task.FromResult(content);
}

static Task<string> ReadDocxFileAsync(Stream stream)
{
    stream.Position = 0;
    using var document = WordprocessingDocument.Open(stream, false);
    var text = document.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;
    return Task.FromResult(text);
}

static List<string> ChunkText(string text, int chunkSize, int overlap)
{
    var normalized = text.Trim();
    if (string.IsNullOrWhiteSpace(normalized))
    {
        return [];
    }

    var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (words.Length == 0)
    {
        return [];
    }

    var chunks = new List<string>();
    var index = 0;
    var step = Math.Max(1, chunkSize - overlap);
    while (index < words.Length)
    {
        var length = Math.Min(chunkSize, words.Length - index);
        chunks.Add(string.Join(' ', words.Skip(index).Take(length)));
        if (index + length >= words.Length)
        {
            break;
        }

        index += step;
    }

    return chunks;
}

static async Task<DocumentIngestionJob?> GetDocumentIngestionJobAsync(string connectionString, Guid documentId)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = """
SELECT DocumentId, FileName, BlobName, Status
FROM dbo.DocumentIngestionJobs
WHERE DocumentId = @documentId;
""";
    command.Parameters.AddWithValue("@documentId", documentId);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }

    return new DocumentIngestionJob(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3));
}

static async Task UpdateDocumentIngestionJobStatusAsync(
    string connectionString,
    Guid documentId,
    string status,
    int progressPercent,
    string? errorMessage,
    int? totalChunks = null,
    bool isReady = false)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = """
UPDATE dbo.DocumentIngestionJobs
SET Status = @status,
    ProgressPercent = @progressPercent,
    ErrorMessage = @errorMessage,
    TotalChunks = COALESCE(@totalChunks, TotalChunks),
    ReadyAtUtc = CASE WHEN @isReady = 1 THEN SYSUTCDATETIME() ELSE ReadyAtUtc END,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE DocumentId = @documentId;
""";
    command.Parameters.AddWithValue("@documentId", documentId);
    command.Parameters.AddWithValue("@status", status);
    command.Parameters.AddWithValue("@progressPercent", progressPercent);
    command.Parameters.AddWithValue("@errorMessage", (object?)errorMessage ?? DBNull.Value);
    command.Parameters.AddWithValue("@totalChunks", (object?)totalChunks ?? DBNull.Value);
    command.Parameters.AddWithValue("@isReady", isReady ? 1 : 0);
    await command.ExecuteNonQueryAsync();
}

static async Task RequeueNonTerminalJobsAsync(string connectionString)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = """
UPDATE dbo.DocumentIngestionJobs
SET Status = 'Queued',
    ProgressPercent = CASE WHEN ProgressPercent < 15 THEN 15 ELSE ProgressPercent END,
    ErrorMessage = NULL,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE Status IN ('Processing', 'Extracting', 'Chunking', 'Embedding', 'Indexing');
""";
    await command.ExecuteNonQueryAsync();
}

static async Task<DocumentIngestionJob?> TryClaimNextQueuedJobAsync(string connectionString)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = """
;WITH next_job AS (
    SELECT TOP (1) DocumentId
    FROM dbo.DocumentIngestionJobs WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE Status = 'Queued'
    ORDER BY UpdatedAtUtc ASC
)
UPDATE jobs
SET Status = 'Processing',
    ProgressPercent = CASE WHEN jobs.ProgressPercent < 20 THEN 20 ELSE jobs.ProgressPercent END,
    ErrorMessage = NULL,
    UpdatedAtUtc = SYSUTCDATETIME()
OUTPUT inserted.DocumentId, inserted.FileName, inserted.BlobName, inserted.Status
FROM dbo.DocumentIngestionJobs jobs
INNER JOIN next_job ON jobs.DocumentId = next_job.DocumentId;
""";

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }

    return new DocumentIngestionJob(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3));
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
        throw new InvalidOperationException($"SQL configuration is incomplete. Missing: {string.Join(", ", missing)}.");
    }

    return $"Server=tcp:{sqlServer},1433;Initial Catalog={sqlDatabase};"
         + $"Authentication=Active Directory Managed Identity;User Id={uamiClientId};"
         + "Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";
}

internal sealed record WorkerIngestRequest(Guid DocumentId);
internal sealed record DocumentIngestionJob(Guid DocumentId, string FileName, string BlobName, string Status);
internal sealed class SearchChunkDocument
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string ChunkId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public float[] ContentVector { get; set; } = [];
}
