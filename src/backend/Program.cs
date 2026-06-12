using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AIHub.Backend.Features.AiPing;
using AIHub.Backend.Features.Health;
using AIHub.Backend.Infrastructure.Ai;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Dapr.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
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
builder.Services.AddKernel();
builder.Services.AddSingleton<Kernel>(_ =>
{
    var kernel = Kernel.CreateBuilder()
        .AddOpenAIChatCompletion(
            modelId: azureOpenAiSettings.ModelId,
            apiKey: azureOpenAiSettings.ApiKey,
            endpoint: azureOpenAiSettings.Endpoint)
        .Build();
    return kernel;
});
builder.Services.AddSingleton<IAiService, SemanticKernelService>();

var app = builder.Build();

var sqlConnectionString = GetSqlConnectionString(app.Configuration);
await EnsureSqlSchemaAsync(sqlConnectionString);

var workerDaprBaseUrl = Environment.GetEnvironmentVariable("WORKER_DAPR_BASE_URL")
    ?? "http://localhost:3500/v1.0/invoke/aihub-worker/method";
var storageConnectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING") ?? string.Empty;
var storageContainerName = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONTAINER_NAME") ?? string.Empty;
var searchEndpoint = Environment.GetEnvironmentVariable("AZURE_SEARCH_ENDPOINT") ?? string.Empty;
var searchIndexName = Environment.GetEnvironmentVariable("AZURE_SEARCH_INDEX_NAME") ?? string.Empty;
var searchApiKey = Environment.GetEnvironmentVariable("AZURE_SEARCH_API_KEY") ?? string.Empty;
var embeddingModelId = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_MODEL_ID")
    ?? azureOpenAiSettings.ModelId;
var uploadConfigured = !string.IsNullOrWhiteSpace(storageConnectionString) && !string.IsNullOrWhiteSpace(storageContainerName);

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

app.MapPost("/v1/uploads/signed-url", async (CreateSignedUploadRequest request) =>
{
    if (!uploadConfigured)
    {
        return Results.BadRequest("Upload pipeline is not configured. Missing storage connection settings.");
    }

    if (string.IsNullOrWhiteSpace(request.FileName))
    {
        return Results.BadRequest("fileName is required.");
    }

    var safeFileName = Path.GetFileName(request.FileName.Trim());
    if (string.IsNullOrWhiteSpace(safeFileName) || safeFileName is "." or "..")
    {
        return Results.BadRequest("fileName must include a valid file name.");
    }

    if (safeFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
    {
        return Results.BadRequest("fileName contains invalid characters.");
    }

    var documentId = Guid.NewGuid();
    var blobName = $"{documentId:N}/{safeFileName}";

    var blobServiceClient = new BlobServiceClient(storageConnectionString);
    var containerClient = blobServiceClient.GetBlobContainerClient(storageContainerName);
    await containerClient.CreateIfNotExistsAsync();
    var blobClient = containerClient.GetBlobClient(blobName);

    var uploadExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15);
    var uploadUrl = CreateBlobUploadSasUrl(blobClient, storageConnectionString, uploadExpiresAtUtc);

    await CreateDocumentIngestionJobAsync(
        sqlConnectionString,
        documentId,
        safeFileName,
        blobName,
        "PendingUpload",
        5);

    return Results.Ok(new CreateSignedUploadResponse(
        documentId,
        safeFileName,
        blobName,
        uploadUrl,
        uploadExpiresAtUtc));
});

app.MapPost("/v1/ingest", async (IngestRequest request, IHttpClientFactory httpClientFactory) =>
{
    var job = await GetDocumentIngestionJobAsync(sqlConnectionString, request.DocumentId);
    if (job is null)
    {
        return Results.NotFound($"Document {request.DocumentId} was not found.");
    }

    await UpdateDocumentIngestionJobStatusAsync(sqlConnectionString, request.DocumentId, "Queued", 15, null);

    var workerPayload = new WorkerIngestRequest(request.DocumentId);
    using var httpClient = httpClientFactory.CreateClient();
    using var response = await httpClient.PostAsJsonAsync($"{workerDaprBaseUrl}/v1/ingest", workerPayload);
    if (!response.IsSuccessStatusCode)
    {
        await UpdateDocumentIngestionJobStatusAsync(
            sqlConnectionString,
            request.DocumentId,
            "Failed",
            100,
            $"Worker trigger failed with HTTP {(int)response.StatusCode}");
        return Results.StatusCode(502);
    }

    return Results.Accepted($"/v1/uploads/{request.DocumentId}/status", new { request.DocumentId, status = "Queued" });
});

app.MapGet("/v1/uploads/{documentId:guid}/status", async (Guid documentId) =>
{
    var status = await GetDocumentIngestionJobAsync(sqlConnectionString, documentId);
    return status is null ? Results.NotFound() : Results.Ok(status);
});

app.MapPost("/v1/chat", async (ChatRequest request, IAiService aiService, IHttpClientFactory httpClientFactory) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest("message is required.");
    }

    var mode = (request.Mode ?? "general").Trim().ToLowerInvariant();
    if (mode is "docs" or "rag")
    {
        if (string.IsNullOrWhiteSpace(searchEndpoint) || string.IsNullOrWhiteSpace(searchIndexName) || string.IsNullOrWhiteSpace(searchApiKey))
        {
            return Results.BadRequest("RAG mode is not configured. Missing search endpoint, index name, or API key.");
        }

        var embedding = await GenerateEmbeddingAsync(
            request.Message,
            azureOpenAiSettings.Endpoint,
            azureOpenAiSettings.ApiKey,
            embeddingModelId,
            httpClientFactory);

        var searchResults = await SearchRelevantChunksAsync(
            embedding,
            searchEndpoint,
            searchIndexName,
            searchApiKey,
            request.DocumentId,
            httpClientFactory);

        var citations = new List<ChatCitation>();
        var contextParts = new List<string>();

        foreach (var result in searchResults)
        {
            if (string.IsNullOrWhiteSpace(result.Content))
            {
                continue;
            }

            contextParts.Add(result.Content);
            citations.Add(new ChatCitation(
                result.DocumentId,
                result.ChunkId,
                result.FileName));
        }

        if (contextParts.Count == 0)
        {
            return Results.Ok(new ChatResponse(
                "I could not find relevant indexed content for this question yet. Upload and ingest a file first.",
                citations));
        }

        var prompt = BuildRagPrompt(request.Message, contextParts);
        var answer = await aiService.InvokePromptAsync(prompt);
        return Results.Ok(new ChatResponse(answer, citations));
    }

    var generalAnswer = await aiService.InvokePromptAsync(request.Message);
    return Results.Ok(new ChatResponse(generalAnswer, []));
});

app.MapGet("/v1/customers", async () =>
{
    var customers = await GetCustomersAsync(sqlConnectionString);
    return Results.Ok(customers);
});

app.MapPost("/v1/customers", async (CreateCustomerRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Name)
        || string.IsNullOrWhiteSpace(request.Email)
        || string.IsNullOrWhiteSpace(request.City)
        || string.IsNullOrWhiteSpace(request.Status))
    {
        return Results.BadRequest("Name, email, city, and status are required.");
    }

    var id = await CreateCustomerAsync(sqlConnectionString, request);
    return Results.Created($"/v1/customers/{id}", new { id });
});

app.MapPut("/v1/customers/{id:int}", async (int id, UpdateCustomerRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Name)
        || string.IsNullOrWhiteSpace(request.Email)
        || string.IsNullOrWhiteSpace(request.City)
        || string.IsNullOrWhiteSpace(request.Status))
    {
        return Results.BadRequest("Name, email, city, and status are required.");
    }

    var updated = await UpdateCustomerAsync(sqlConnectionString, id, request);
    return updated ? Results.NoContent() : Results.NotFound();
});

app.MapDelete("/v1/customers/{id:int}", async (int id) =>
{
    var deleted = await DeleteCustomerAsync(sqlConnectionString, id);
    return deleted ? Results.NoContent() : Results.NotFound();
});

app.MapGet("/", () => "AI Hub Backend is running!");

app.Run();

static string BuildRagPrompt(string question, IEnumerable<string> contexts)
{
    var contextBlock = string.Join("\n\n---\n\n", contexts);
    return $"""
You are an enterprise copilot assistant.
Answer only from the provided context. If context is insufficient, say so.

Context:
{contextBlock}

Question:
{question}
""";
}

static string CreateBlobUploadSasUrl(BlobClient blobClient, string connectionString, DateTimeOffset expiresAtUtc)
{
    var accountName = GetStorageConnectionValue(connectionString, "AccountName");
    var accountKey = GetStorageConnectionValue(connectionString, "AccountKey");
    var credential = new StorageSharedKeyCredential(accountName, accountKey);

    var builder = new BlobSasBuilder
    {
        BlobContainerName = blobClient.BlobContainerName,
        BlobName = blobClient.Name,
        Resource = "b",
        ExpiresOn = expiresAtUtc
    };
    builder.SetPermissions(BlobSasPermissions.Create | BlobSasPermissions.Write | BlobSasPermissions.Add);

    var sasToken = builder.ToSasQueryParameters(credential).ToString();
    return $"{blobClient.Uri}?{sasToken}";
}

static string GetStorageConnectionValue(string connectionString, string key)
{
    var segments = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    foreach (var segment in segments)
    {
        var kvp = segment.Split('=', 2, StringSplitOptions.TrimEntries);
        if (kvp.Length == 2 && string.Equals(kvp[0], key, StringComparison.OrdinalIgnoreCase))
        {
            return kvp[1];
        }
    }

    throw new InvalidOperationException($"Storage connection string is missing '{key}'.");
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
    var embedding = document.RootElement
        .GetProperty("data")[0]
        .GetProperty("embedding")
        .EnumerateArray()
        .Select(x => x.GetSingle())
        .ToArray();
    return embedding;
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

static async Task<List<SearchChunkDocument>> SearchRelevantChunksAsync(
    float[] embedding,
    string searchEndpoint,
    string searchIndexName,
    string searchApiKey,
    Guid? documentId,
    IHttpClientFactory httpClientFactory)
{
    using var client = httpClientFactory.CreateClient();
    client.DefaultRequestHeaders.Add("api-key", searchApiKey);

    var requestUri = $"{searchEndpoint.TrimEnd('/')}/indexes/{searchIndexName}/docs/search?api-version=2024-07-01";
    var requestBody = new Dictionary<string, object?>
    {
        ["search"] = "*",
        ["top"] = 5,
        ["select"] = "id,documentId,chunkId,fileName,content",
        ["vectorQueries"] = new[]
        {
            new Dictionary<string, object?>
            {
                ["kind"] = "vector",
                ["fields"] = "contentVector",
                ["k"] = 5,
                ["vector"] = embedding
            }
        }
    };

    if (documentId.HasValue)
    {
        var escapedDocumentId = documentId.Value.ToString().Replace("'", "''");
        requestBody["filter"] = $"documentId eq '{escapedDocumentId}'";
    }

    using var payload = new StringContent(
        JsonSerializer.Serialize(requestBody),
        Encoding.UTF8,
        "application/json");
    using var response = await client.PostAsync(requestUri, payload);
    response.EnsureSuccessStatusCode();

    var json = await response.Content.ReadAsStringAsync();
    using var document = JsonDocument.Parse(json);
    var chunks = new List<SearchChunkDocument>();
    if (!document.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
    {
        return chunks;
    }

    foreach (var element in values.EnumerateArray())
    {
        chunks.Add(new SearchChunkDocument
        {
            Id = element.GetProperty("id").GetString() ?? string.Empty,
            DocumentId = element.GetProperty("documentId").GetString() ?? string.Empty,
            ChunkId = element.GetProperty("chunkId").GetString() ?? string.Empty,
            FileName = element.GetProperty("fileName").GetString() ?? string.Empty,
            Content = element.GetProperty("content").GetString() ?? string.Empty
        });
    }

    return chunks;
}

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

static async Task CreateDocumentIngestionJobAsync(
    string connectionString,
    Guid documentId,
    string fileName,
    string blobName,
    string status,
    int progressPercent)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = """
MERGE dbo.DocumentIngestionJobs AS target
USING (SELECT @documentId AS DocumentId) AS source
ON target.DocumentId = source.DocumentId
WHEN MATCHED THEN
  UPDATE SET FileName = @fileName,
             BlobName = @blobName,
             Status = @status,
             ProgressPercent = @progressPercent,
             ErrorMessage = NULL,
             UpdatedAtUtc = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
  INSERT (DocumentId, FileName, BlobName, Status, ProgressPercent, CreatedAtUtc, UpdatedAtUtc)
  VALUES (@documentId, @fileName, @blobName, @status, @progressPercent, SYSUTCDATETIME(), SYSUTCDATETIME());
""";
    command.Parameters.AddWithValue("@documentId", documentId);
    command.Parameters.AddWithValue("@fileName", fileName);
    command.Parameters.AddWithValue("@blobName", blobName);
    command.Parameters.AddWithValue("@status", status);
    command.Parameters.AddWithValue("@progressPercent", progressPercent);
    await command.ExecuteNonQueryAsync();
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

static async Task<DocumentIngestionStatus?> GetDocumentIngestionJobAsync(string connectionString, Guid documentId)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = """
SELECT DocumentId, FileName, BlobName, Status, ProgressPercent, TotalChunks, ErrorMessage, CreatedAtUtc, UpdatedAtUtc, ReadyAtUtc
FROM dbo.DocumentIngestionJobs
WHERE DocumentId = @documentId;
""";
    command.Parameters.AddWithValue("@documentId", documentId);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }

    return new DocumentIngestionStatus(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetInt32(4),
        reader.IsDBNull(5) ? null : reader.GetInt32(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.GetDateTime(7),
        reader.GetDateTime(8),
        reader.IsDBNull(9) ? null : reader.GetDateTime(9));
}

static AzureOpenAiSettings ResolveAzureOpenAiSettings(IConfiguration configuration)
{
    var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
        ?? configuration[$"{AzureOpenAiOptions.SectionName}:ApiKey"];
    var modelId = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_ID")
        ?? configuration[$"{AzureOpenAiOptions.SectionName}:ModelId"];
    var endpointText = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
        ?? configuration[$"{AzureOpenAiOptions.SectionName}:Endpoint"];

    var issues = new List<string>();
    if (string.IsNullOrWhiteSpace(apiKey)) issues.Add("AZURE_OPENAI_API_KEY is missing");
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

    return new AzureOpenAiSettings(apiKey!, modelId!, new Uri(endpointText!, UriKind.Absolute));
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

static async Task<List<CustomerRecord>> GetCustomersAsync(string connectionString)
{
    var customers = new List<CustomerRecord>();
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = """
SELECT Id, Name, Email, City, Status
FROM dbo.Customers
ORDER BY Id;
""";

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        customers.Add(new CustomerRecord(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4)));
    }

    return customers;
}

static async Task<int> CreateCustomerAsync(string connectionString, CreateCustomerRequest request)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = """
INSERT INTO dbo.Customers (Name, Email, City, Status)
OUTPUT INSERTED.Id
VALUES (@name, @email, @city, @status);
""";
    command.Parameters.AddWithValue("@name", request.Name.Trim());
    command.Parameters.AddWithValue("@email", request.Email.Trim());
    command.Parameters.AddWithValue("@city", request.City.Trim());
    command.Parameters.AddWithValue("@status", request.Status.Trim());

    var result = await command.ExecuteScalarAsync();
    return Convert.ToInt32(result);
}

static async Task<bool> UpdateCustomerAsync(string connectionString, int id, UpdateCustomerRequest request)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = """
UPDATE dbo.Customers
SET Name = @name,
    Email = @email,
    City = @city,
    Status = @status
WHERE Id = @id;
""";
    command.Parameters.AddWithValue("@id", id);
    command.Parameters.AddWithValue("@name", request.Name.Trim());
    command.Parameters.AddWithValue("@email", request.Email.Trim());
    command.Parameters.AddWithValue("@city", request.City.Trim());
    command.Parameters.AddWithValue("@status", request.Status.Trim());

    var affected = await command.ExecuteNonQueryAsync();
    return affected > 0;
}

static async Task<bool> DeleteCustomerAsync(string connectionString, int id)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = "DELETE FROM dbo.Customers WHERE Id = @id;";
    command.Parameters.AddWithValue("@id", id);

    var affected = await command.ExecuteNonQueryAsync();
    return affected > 0;
}

internal sealed record AzureOpenAiSettings(string ApiKey, string ModelId, Uri Endpoint);
internal sealed record CustomerRecord(int Id, string Name, string Email, string City, string Status);
internal sealed record CreateCustomerRequest(string Name, string Email, string City, string Status);
internal sealed record UpdateCustomerRequest(string Name, string Email, string City, string Status);
internal sealed record CreateSignedUploadRequest(string FileName);
internal sealed record CreateSignedUploadResponse(
    Guid DocumentId,
    string FileName,
    string BlobName,
    string UploadUrl,
    DateTimeOffset ExpiresAtUtc);
internal sealed record IngestRequest(Guid DocumentId);
internal sealed record WorkerIngestRequest(Guid DocumentId);
internal sealed record DocumentIngestionStatus(
    Guid DocumentId,
    string FileName,
    string BlobName,
    string Status,
    int ProgressPercent,
    int? TotalChunks,
    string? ErrorMessage,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? ReadyAtUtc);
internal sealed record ChatRequest(string Message, string? Mode, Guid? DocumentId);
internal sealed record ChatCitation(string DocumentId, string ChunkId, string FileName);
internal sealed record ChatResponse(string Answer, IReadOnlyList<ChatCitation> Citations);
internal sealed class SearchChunkDocument
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string ChunkId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
