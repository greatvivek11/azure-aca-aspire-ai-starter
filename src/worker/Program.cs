using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
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

var sqlConnectionString = GetSqlConnectionString(app.Configuration);
var aiMode = (Environment.GetEnvironmentVariable("AI_MODE") ?? "azure").Trim().ToLowerInvariant();
if (string.Equals(aiMode, "local", StringComparison.OrdinalIgnoreCase))
{
    await EnsureDatabaseExistsAsync(sqlConnectionString);
}
else
{
    app.Logger.LogInformation("Skipping SQL database creation check in Azure mode.");
}
var storageAccountName = Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_NAME") ?? string.Empty;
var storageConnectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING") ?? string.Empty;
var storageContainerName = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONTAINER_NAME") ?? string.Empty;
var storageAuthMode = (Environment.GetEnvironmentVariable("AZURE_STORAGE_AUTH_MODE") ?? "managed-identity")
    .Trim()
    .ToLowerInvariant();
var searchEndpoint = Environment.GetEnvironmentVariable("AZURE_SEARCH_ENDPOINT") ?? string.Empty;
var searchIndexName = Environment.GetEnvironmentVariable("AZURE_SEARCH_INDEX_NAME") ?? string.Empty;
var searchApiKey = Environment.GetEnvironmentVariable("AZURE_SEARCH_API_KEY") ?? string.Empty;
var qdrantUrl = (Environment.GetEnvironmentVariable("QDRANT_URL") ?? "http://qdrant:6333").Trim();
var qdrantCollection = (Environment.GetEnvironmentVariable("QDRANT_COLLECTION") ?? "documents").Trim();
var ollamaBaseUrl = (Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://ollama:11434").Trim();
var openAiEndpointText = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? string.Empty;
var openAiApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ?? string.Empty;
var openAiAuthMode = (Environment.GetEnvironmentVariable("AZURE_OPENAI_AUTH_MODE") ?? "api-key")
    .Trim()
    .ToLowerInvariant();
var managedIdentityClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
var embeddingModelId = aiMode == "local"
    ? (Environment.GetEnvironmentVariable("OLLAMA_EMBED_MODEL") ?? "nomic-embed-text")
    : (Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_MODEL_ID") ?? string.Empty);
var embeddingDimensions = aiMode == "local"
    ? (int.TryParse(Environment.GetEnvironmentVariable("OLLAMA_EMBED_DIMENSIONS"), out var parsedLocalDimensions)
        ? parsedLocalDimensions
        : 768)
    : (int.TryParse(Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DIMENSIONS"), out var parsed)
        ? parsed
        : 1536);
var storageConfigured =
    (!string.IsNullOrWhiteSpace(storageConnectionString) || !string.IsNullOrWhiteSpace(storageAccountName))
    && !string.IsNullOrWhiteSpace(storageContainerName);
var azureIngestionConfigured =
    storageConfigured &&
    !string.IsNullOrWhiteSpace(searchEndpoint) &&
    !string.IsNullOrWhiteSpace(searchIndexName) &&
    !string.IsNullOrWhiteSpace(searchApiKey) &&
    !string.IsNullOrWhiteSpace(openAiEndpointText) &&
    (!string.Equals(openAiAuthMode, "api-key", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(openAiApiKey)) &&
    !string.IsNullOrWhiteSpace(embeddingModelId);
var localIngestionConfigured =
    storageConfigured &&
    !string.IsNullOrWhiteSpace(qdrantUrl) &&
    !string.IsNullOrWhiteSpace(qdrantCollection) &&
    !string.IsNullOrWhiteSpace(ollamaBaseUrl) &&
    !string.IsNullOrWhiteSpace(embeddingModelId);
var ingestionConfigured = aiMode == "local" ? localIngestionConfigured : azureIngestionConfigured;

app.Logger.LogInformation(
    "Worker startup configuration resolved. AiMode={AiMode}, IngestionConfigured={IngestionConfigured}, StorageConfigured={StorageConfigured}, QdrantUrlConfigured={QdrantConfigured}, SearchConfigured={SearchConfigured}, OpenAiEndpointConfigured={OpenAiEndpointConfigured}",
    aiMode,
    ingestionConfigured,
    storageConfigured,
    !string.IsNullOrWhiteSpace(qdrantUrl),
    !string.IsNullOrWhiteSpace(searchEndpoint) && !string.IsNullOrWhiteSpace(searchIndexName),
    !string.IsNullOrWhiteSpace(openAiEndpointText));

if (string.Equals(aiMode, "local", StringComparison.OrdinalIgnoreCase))
{
    using var modelWarmupClient = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient();
    await WarmLocalOllamaModelsAsync(modelWarmupClient, ollamaBaseUrl, [embeddingModelId], app.Logger);
}

if (ingestionConfigured)
{
    await EnsureVectorStoreAsync(aiMode, searchEndpoint, searchApiKey, searchIndexName, qdrantUrl, qdrantCollection, embeddingDimensions, app.Logger);
    await EnsureWorkerSqlSchemaAsync(sqlConnectionString);
    await RequeueNonTerminalJobsAsync(sqlConnectionString);
    _ = RunIngestionLoopAsync(
        aiMode,
        sqlConnectionString,
        storageAccountName,
        storageConnectionString,
        storageContainerName,
        storageAuthMode,
        searchEndpoint,
        searchApiKey,
        searchIndexName,
        qdrantUrl,
        qdrantCollection,
        ollamaBaseUrl,
        string.IsNullOrWhiteSpace(openAiEndpointText) ? new Uri("http://localhost") : new Uri(openAiEndpointText, UriKind.Absolute),
        openAiApiKey,
        openAiAuthMode,
        managedIdentityClientId,
        embeddingModelId,
        embeddingDimensions,
        httpClientFactory: app.Services.GetRequiredService<IHttpClientFactory>(),
        app.Logger,
        app.Lifetime.ApplicationStopping);
}
else
{
    app.Logger.LogWarning(
        "Worker ingestion pipeline disabled. AiMode={AiMode}, StorageConfigured={StorageConfigured}, LocalIngestionConfigured={LocalConfigured}, AzureIngestionConfigured={AzureConfigured}",
        aiMode,
        storageConfigured,
        localIngestionConfigured,
        azureIngestionConfigured);
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

app.MapGet("/", () => "ACA Aspire AI Starter Worker is running!");
app.Run();

static async Task RunIngestionLoopAsync(
    string aiMode,
    string sqlConnectionString,
    string storageAccountName,
    string storageConnectionString,
    string storageContainerName,
    string storageAuthMode,
    string searchEndpoint,
    string searchApiKey,
    string searchIndexName,
    string qdrantUrl,
    string qdrantCollection,
    string ollamaBaseUrl,
    Uri openAiEndpoint,
    string openAiApiKey,
    string openAiAuthMode,
    string? managedIdentityClientId,
    string embeddingModelId,
    int embeddingDimensions,
    IHttpClientFactory httpClientFactory,
    ILogger logger,
    CancellationToken cancellationToken)
{
    logger.LogInformation("Starting ingestion queue processor.");
    var consecutiveLoopFailures = 0;

    while (!cancellationToken.IsCancellationRequested)
    {
        DocumentIngestionJob? claimedJob = null;
        try
        {
            claimedJob = await TryClaimNextQueuedJobAsync(sqlConnectionString);
            if (claimedJob is null)
            {
                consecutiveLoopFailures = 0;
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                continue;
            }

            await ProcessDocumentAsync(
                aiMode,
                claimedJob.DocumentId,
                sqlConnectionString,
                storageAccountName,
                storageConnectionString,
                storageContainerName,
                storageAuthMode,
                searchEndpoint,
                searchApiKey,
                searchIndexName,
                qdrantUrl,
                qdrantCollection,
                ollamaBaseUrl,
                openAiEndpoint,
                openAiApiKey,
                openAiAuthMode,
                managedIdentityClientId,
                embeddingModelId,
                embeddingDimensions,
                httpClientFactory,
                logger);

            consecutiveLoopFailures = 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            break;
        }
        catch (Exception ex)
        {
            consecutiveLoopFailures++;
            var backoff = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(consecutiveLoopFailures, 5))));

            if (claimedJob is not null)
            {
                logger.LogError(ex, "Unexpected error while processing document {DocumentId}", claimedJob.DocumentId);
                await UpdateDocumentIngestionJobStatusAsync(
                    sqlConnectionString,
                    claimedJob.DocumentId,
                    "Failed",
                    100,
                    ex.Message);
            }
            else
            {
                logger.LogError(ex, "Unexpected error while claiming queued ingestion jobs.");
            }

            try
            {
                await Task.Delay(backoff, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    logger.LogInformation("Stopping ingestion queue processor.");
}

static async Task ProcessDocumentAsync(
    string aiMode,
    Guid documentId,
    string sqlConnectionString,
    string storageAccountName,
    string storageConnectionString,
    string storageContainerName,
    string storageAuthMode,
    string searchEndpoint,
    string searchApiKey,
    string searchIndexName,
    string qdrantUrl,
    string qdrantCollection,
    string ollamaBaseUrl,
    Uri openAiEndpoint,
    string openAiApiKey,
    string openAiAuthMode,
    string? managedIdentityClientId,
    string embeddingModelId,
    int embeddingDimensions,
    IHttpClientFactory httpClientFactory,
    ILogger logger)
{
    var job = await GetDocumentIngestionJobAsync(sqlConnectionString, documentId);
    if (job is null)
    {
        throw new InvalidOperationException($"Document {documentId} does not exist.");
    }

    await UpdateDocumentIngestionJobStatusAsync(sqlConnectionString, documentId, "Extracting", 25, null);

    await using var blobStream = await OpenBlobReadStreamAsync(
        storageAccountName,
        storageConnectionString,
        storageContainerName,
        job.BlobName,
        storageAuthMode,
        managedIdentityClientId,
        httpClientFactory);
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
            aiMode,
            chunk,
            ollamaBaseUrl,
            openAiEndpoint,
            openAiApiKey,
            openAiAuthMode,
            embeddingModelId,
            embeddingDimensions,
            managedIdentityClientId,
            httpClientFactory);

        searchDocuments.Add(new SearchChunkDocument
        {
            Id = CreateQdrantPointId(documentId, index),
            DocumentId = documentId.ToString(),
            ChunkId = $"chunk-{index + 1}",
            FileName = job.FileName,
            Content = chunk,
            ContentVector = embedding
        });
    }

    static string CreateQdrantPointId(Guid documentId, int chunkIndex)
    {
        var source = $"{documentId:N}:{chunkIndex:D5}";
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(source));
        return new Guid(bytes).ToString("D");
    }

    await UpdateDocumentIngestionJobStatusAsync(sqlConnectionString, documentId, "Indexing", 85, null);

    if (string.Equals(aiMode, "local", StringComparison.OrdinalIgnoreCase))
    {
        await UpsertQdrantDocumentsAsync(qdrantUrl, qdrantCollection, searchDocuments, httpClientFactory);
    }
    else
    {
        var searchClient = new SearchClient(
            new Uri(searchEndpoint),
            searchIndexName,
            new AzureKeyCredential(searchApiKey));
        await searchClient.MergeOrUploadDocumentsAsync(searchDocuments);
    }

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

static async Task EnsureVectorStoreAsync(
    string aiMode,
    string searchEndpoint,
    string searchApiKey,
    string searchIndexName,
    string qdrantUrl,
    string qdrantCollection,
    int embeddingDimensions,
    ILogger logger)
{
    if (string.Equals(aiMode, "local", StringComparison.OrdinalIgnoreCase))
    {
        logger.LogInformation(
            "Ensuring Qdrant collection. Url={QdrantUrl}, Collection={Collection}, Dimensions={Dimensions}",
            qdrantUrl,
            qdrantCollection,
            embeddingDimensions);
        await EnsureQdrantCollectionAsync(qdrantUrl, qdrantCollection, embeddingDimensions, logger);
        return;
    }

    logger.LogInformation(
        "Ensuring Azure AI Search index. Endpoint={SearchEndpoint}, Index={IndexName}, Dimensions={Dimensions}",
        searchEndpoint,
        searchIndexName,
        embeddingDimensions);
    await EnsureSearchIndexAsync(searchEndpoint, searchApiKey, searchIndexName, embeddingDimensions);
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

static async Task EnsureQdrantCollectionAsync(string qdrantUrl, string collectionName, int embeddingDimensions, ILogger logger)
{
    using var client = new HttpClient();
    using var response = await CreateQdrantCollectionAsync(client, qdrantUrl, collectionName, embeddingDimensions);
    if (response.IsSuccessStatusCode)
    {
        return;
    }

    var responseBody = await response.Content.ReadAsStringAsync();
    if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
    {
        var existingDimensions = await TryGetQdrantCollectionSizeAsync(client, qdrantUrl, collectionName);
        if (existingDimensions.HasValue && existingDimensions.Value != embeddingDimensions)
        {
            logger.LogWarning(
                "Qdrant collection dimensions mismatch detected. Recreating collection. Collection={Collection}, ExistingDimensions={ExistingDimensions}, ExpectedDimensions={ExpectedDimensions}",
                collectionName,
                existingDimensions.Value,
                embeddingDimensions);

            using var deleteResponse = await client.DeleteAsync(
                $"{qdrantUrl.TrimEnd('/')}/collections/{collectionName}");
            var deleteResponseBody = await deleteResponse.Content.ReadAsStringAsync();
            if (!deleteResponse.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Failed to delete mismatched Qdrant collection '{collectionName}'. Status={(int)deleteResponse.StatusCode}. Body={deleteResponseBody}");
            }

            using var recreateResponse = await CreateQdrantCollectionAsync(client, qdrantUrl, collectionName, embeddingDimensions);
            var recreateResponseBody = await recreateResponse.Content.ReadAsStringAsync();
            if (!recreateResponse.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Failed to recreate Qdrant collection '{collectionName}'. Status={(int)recreateResponse.StatusCode}. Body={recreateResponseBody}");
            }

            logger.LogInformation(
                "Recreated Qdrant collection with expected dimensions. Collection={Collection}, Dimensions={Dimensions}",
                collectionName,
                embeddingDimensions);
            return;
        }

        logger.LogInformation(
            "Qdrant collection already exists; continuing startup. Collection={Collection}, Response={ResponseBody}",
            collectionName,
            responseBody);
        return;
    }

    throw new HttpRequestException(
        $"Failed to ensure Qdrant collection '{collectionName}'. Status={(int)response.StatusCode}. Body={responseBody}");
}

static Task<HttpResponseMessage> CreateQdrantCollectionAsync(HttpClient client, string qdrantUrl, string collectionName, int embeddingDimensions)
{
    var payload = new StringContent(
        JsonSerializer.Serialize(new
        {
            vectors = new
            {
                size = embeddingDimensions,
                distance = "Cosine"
            }
        }),
        Encoding.UTF8,
        "application/json");
    return client.PutAsync(
        $"{qdrantUrl.TrimEnd('/')}/collections/{collectionName}",
        payload);
}

static async Task<int?> TryGetQdrantCollectionSizeAsync(HttpClient client, string qdrantUrl, string collectionName)
{
    using var response = await client.GetAsync(
        $"{qdrantUrl.TrimEnd('/')}/collections/{collectionName}");
    if (!response.IsSuccessStatusCode)
    {
        return null;
    }

    var responseBody = await response.Content.ReadAsStringAsync();
    using var document = JsonDocument.Parse(responseBody);
    if (!document.RootElement.TryGetProperty("result", out var result) ||
        !result.TryGetProperty("config", out var config) ||
        !config.TryGetProperty("params", out var parameters) ||
        !parameters.TryGetProperty("vectors", out var vectors) ||
        !vectors.TryGetProperty("size", out var sizeElement))
    {
        return null;
    }

    return sizeElement.GetInt32();
}

static async Task UpsertQdrantDocumentsAsync(
    string qdrantUrl,
    string collectionName,
    IReadOnlyCollection<SearchChunkDocument> documents,
    IHttpClientFactory httpClientFactory)
{
    var points = documents.Select(document => new
    {
        id = document.Id,
        vector = document.ContentVector,
        payload = new
        {
            id = document.Id,
            documentId = document.DocumentId,
            chunkId = document.ChunkId,
            fileName = document.FileName,
            content = document.Content
        }
    });

    using var payload = new StringContent(
        JsonSerializer.Serialize(new { points }),
        Encoding.UTF8,
        "application/json");
    using var client = httpClientFactory.CreateClient();
    using var response = await client.PutAsync(
        $"{qdrantUrl.TrimEnd('/')}/collections/{collectionName}/points?wait=true",
        payload);
    if (!response.IsSuccessStatusCode)
    {
        var responseBody = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException(
            $"Failed to upsert Qdrant documents for collection '{collectionName}'. Status={(int)response.StatusCode}. Body={responseBody}");
    }
}

static async Task<float[]> GenerateEmbeddingAsync(
    string aiMode,
    string text,
    string ollamaBaseUrl,
    Uri endpoint,
    string? apiKey,
    string openAiAuthMode,
    string embeddingDeployment,
    int embeddingDimensions,
    string? managedIdentityClientId,
    IHttpClientFactory httpClientFactory)
{
    if (string.Equals(aiMode, "local", StringComparison.OrdinalIgnoreCase))
    {
        using var localClient = httpClientFactory.CreateClient();
        await EnsureOllamaModelPulledAsync(localClient, ollamaBaseUrl, embeddingDeployment);
        using var localPayload = new StringContent(
            JsonSerializer.Serialize(new { model = embeddingDeployment, prompt = text }),
            Encoding.UTF8,
            "application/json");
        using var localResponse = await localClient.PostAsync($"{ollamaBaseUrl.TrimEnd('/')}/api/embeddings", localPayload);
        localResponse.EnsureSuccessStatusCode();
        return await ParseLocalEmbeddingAsync(localResponse);
    }

    using var client = httpClientFactory.CreateClient();
    var usingManagedIdentity = await ConfigureOpenAiAuthAsync(client, apiKey, openAiAuthMode, managedIdentityClientId, httpClientFactory);

    var requestUri = BuildEmbeddingsRequestUri(endpoint, embeddingDeployment);
    using var payload = new StringContent(
        JsonSerializer.Serialize(new { input = text, dimensions = embeddingDimensions }),
        Encoding.UTF8,
        "application/json");
    using var response = await client.PostAsync(requestUri, payload);
    if (IsAuthFailure(response.StatusCode) && usingManagedIdentity && !string.IsNullOrWhiteSpace(apiKey))
    {
        using var retryClient = httpClientFactory.CreateClient();
        retryClient.DefaultRequestHeaders.Add("api-key", apiKey);
        using var retryPayload = new StringContent(
            JsonSerializer.Serialize(new { input = text, dimensions = embeddingDimensions }),
            Encoding.UTF8,
            "application/json");
        using var retryResponse = await retryClient.PostAsync(requestUri, retryPayload);
        retryResponse.EnsureSuccessStatusCode();
        return await ParseEmbeddingAsync(retryResponse);
    }

    response.EnsureSuccessStatusCode();
    return await ParseEmbeddingAsync(response);
}

static async Task<Stream> OpenBlobReadStreamAsync(
    string storageAccountName,
    string storageConnectionString,
    string storageContainerName,
    string blobName,
    string storageAuthMode,
    string? managedIdentityClientId,
    IHttpClientFactory httpClientFactory)
{
    if (string.Equals(storageAuthMode, "managed-identity", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(storageAccountName))
    {
        try
        {
            var token = await TryAcquireManagedIdentityTokenAsync(
                "https://storage.azure.com",
                managedIdentityClientId,
                httpClientFactory);
            if (!string.IsNullOrWhiteSpace(token))
            {
                return await DownloadBlobWithManagedIdentityAsync(
                    storageAccountName,
                    storageContainerName,
                    blobName,
                    token,
                    httpClientFactory);
            }
        }
        catch (HttpRequestException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    var blobServiceClient = new BlobServiceClient(storageConnectionString);
    var containerClient = blobServiceClient.GetBlobContainerClient(storageContainerName);
    var blobClient = containerClient.GetBlobClient(blobName);
    if (!await blobClient.ExistsAsync())
    {
        throw new InvalidOperationException($"Blob '{blobName}' was not found.");
    }

    return await blobClient.OpenReadAsync();
}

static async Task<Stream> DownloadBlobWithManagedIdentityAsync(
    string storageAccountName,
    string storageContainerName,
    string blobName,
    string bearerToken,
    IHttpClientFactory httpClientFactory)
{
    using var request = new HttpRequestMessage(
        HttpMethod.Get,
        BuildBlobReadUrl(storageAccountName, storageContainerName, blobName));
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
    request.Headers.Add("x-ms-version", "2023-11-03");

    using var client = httpClientFactory.CreateClient();
    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    response.EnsureSuccessStatusCode();

    await using var sourceStream = await response.Content.ReadAsStreamAsync();
    var buffer = new MemoryStream();
    await sourceStream.CopyToAsync(buffer);
    buffer.Position = 0;
    return buffer;
}

static string BuildBlobReadUrl(string storageAccountName, string storageContainerName, string blobName)
{
    var escapedBlobPath = string.Join(
        "/",
        blobName.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
    return $"https://{storageAccountName}.blob.core.windows.net/{Uri.EscapeDataString(storageContainerName)}/{escapedBlobPath}";
}

static bool IsAuthFailure(System.Net.HttpStatusCode statusCode) =>
    statusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden;

static async Task<float[]> ParseEmbeddingAsync(HttpResponseMessage response)
{
    var json = await response.Content.ReadAsStringAsync();
    using var document = JsonDocument.Parse(json);
    return document.RootElement
        .GetProperty("data")[0]
        .GetProperty("embedding")
        .EnumerateArray()
        .Select(element => element.GetSingle())
        .ToArray();
}

static async Task<float[]> ParseLocalEmbeddingAsync(HttpResponseMessage response)
{
    var json = await response.Content.ReadAsStringAsync();
    using var document = JsonDocument.Parse(json);
    return document.RootElement
        .GetProperty("embedding")
        .EnumerateArray()
        .Select(element => element.GetSingle())
        .ToArray();
}

static async Task EnsureOllamaModelPulledAsync(HttpClient client, string ollamaBaseUrl, string modelName)
{
    using var payload = new StringContent(
        JsonSerializer.Serialize(new { name = modelName, stream = false }),
        Encoding.UTF8,
        "application/json");
    using var response = await client.PostAsync($"{ollamaBaseUrl.TrimEnd('/')}/api/pull", payload);
    if (!response.IsSuccessStatusCode)
    {
        var responseBody = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException(
            $"Ollama failed to pull model '{modelName}' (HTTP {(int)response.StatusCode}). Response: {responseBody}");
    }
}

static async Task WarmLocalOllamaModelsAsync(
    HttpClient client,
    string ollamaBaseUrl,
    IReadOnlyList<string> modelNames,
    ILogger logger)
{
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

    foreach (var modelName in uniqueModelNames)
    {
        logger.LogInformation("Preloading Ollama model {ModelName}.", modelName);
        try
        {
            await EnsureOllamaModelPulledAsync(client, ollamaBaseUrl, modelName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Skipping Ollama model warmup for {ModelName}.", modelName);
            continue;
        }

        logger.LogInformation("Ollama model {ModelName} is ready.", modelName);
    }
}

static async Task<bool> ConfigureOpenAiAuthAsync(
    HttpClient client,
    string? apiKey,
    string openAiAuthMode,
    string? managedIdentityClientId,
    IHttpClientFactory httpClientFactory)
{
    if (string.Equals(openAiAuthMode, "managed-identity", StringComparison.OrdinalIgnoreCase))
    {
        var token = await TryAcquireManagedIdentityTokenAsync(
            "https://cognitiveservices.azure.com",
            managedIdentityClientId,
            httpClientFactory);
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return true;
        }
    }

    if (string.IsNullOrWhiteSpace(apiKey))
    {
        throw new InvalidOperationException(
            "OpenAI/Foundry authentication is unavailable. Set AZURE_OPENAI_API_KEY or provide managed identity runtime.");
    }

    client.DefaultRequestHeaders.Add("api-key", apiKey);
    return false;
}

static async Task<string?> TryAcquireManagedIdentityTokenAsync(
    string resource,
    string? managedIdentityClientId,
    IHttpClientFactory httpClientFactory)
{
    try
    {
        var identityEndpoint = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT");
        var identityHeader = Environment.GetEnvironmentVariable("IDENTITY_HEADER");
        if (string.IsNullOrWhiteSpace(identityEndpoint) || string.IsNullOrWhiteSpace(identityHeader))
        {
            return null;
        }

        var requestUri = $"{identityEndpoint}?api-version=2019-08-01&resource={Uri.EscapeDataString(resource)}";
        if (!string.IsNullOrWhiteSpace(managedIdentityClientId))
        {
            requestUri = $"{requestUri}&client_id={Uri.EscapeDataString(managedIdentityClientId)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Add("X-IDENTITY-HEADER", identityHeader);

        using var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("access_token", out var tokenElement))
        {
            return null;
        }

        return tokenElement.GetString();
    }
    catch (HttpRequestException)
    {
        return null;
    }
    catch (InvalidOperationException)
    {
        return null;
    }
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

static async Task EnsureWorkerSqlSchemaAsync(string connectionString)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = """
IF OBJECT_ID(N'dbo.DocumentIngestionJobs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentIngestionJobs (
        DocumentId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        FileName NVARCHAR(260) NOT NULL,
        BlobName NVARCHAR(512) NOT NULL,
        Status NVARCHAR(40) NOT NULL,
        ProgressPercent INT NOT NULL CONSTRAINT DF_DocumentIngestionJobs_Progress DEFAULT (0),
        TotalChunks INT NULL,
        ErrorMessage NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_DocumentIngestionJobs_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_DocumentIngestionJobs_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        ReadyAtUtc DATETIME2 NULL
    );
END;
""";
    await command.ExecuteNonQueryAsync();
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
