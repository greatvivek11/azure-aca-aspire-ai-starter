using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;

namespace AIHub.Backend.Features.DocumentIngestion;

public static class Endpoint
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".pdf",
        ".docx"
    };

    public static void MapDocumentIngestionEndpoints(this IEndpointRouteBuilder app, DocumentIngestionOptions options)
    {
        app.MapPost("/v1/uploads/signed-url", async (CreateSignedUploadRequest request, IHttpClientFactory httpClientFactory) =>
        {
            if (!options.UploadConfigured)
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

            var extension = Path.GetExtension(safeFileName);
            if (string.IsNullOrWhiteSpace(extension) || !SupportedExtensions.Contains(extension))
            {
                return Results.BadRequest("Only .txt, .pdf, and .docx files are supported for ingestion.");
            }

            var documentId = Guid.NewGuid();
            var blobName = $"{documentId:N}/{safeFileName}";
            var resolvedStorageAccountName = ResolveStorageAccountName(options.StorageAccountName, options.StorageConnectionString);
            BlobClient blobClient;
            if (!string.IsNullOrWhiteSpace(options.StorageConnectionString))
            {
                var blobServiceClient = new BlobServiceClient(options.StorageConnectionString);
                var containerClient = blobServiceClient.GetBlobContainerClient(options.StorageContainerName);
                await containerClient.CreateIfNotExistsAsync();
                blobClient = containerClient.GetBlobClient(blobName);
            }
            else
            {
                blobClient = new BlobClient(BuildBlobUri(resolvedStorageAccountName, options.StorageContainerName, blobName));
            }

            var uploadExpiresAtUtc = DateTimeOffset.UtcNow.Add(options.UploadUrlLifetime);
            var uploadUrl = await CreateBlobUploadSasUrlAsync(blobClient, options, uploadExpiresAtUtc, httpClientFactory);

            await CreateDocumentIngestionJobAsync(
                options.SqlConnectionString,
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

        app.MapPost("/v1/ingest", async (IngestRequest request, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("DocumentIngestion");
            var job = await GetDocumentIngestionJobAsync(options.SqlConnectionString, request.DocumentId);
            if (job is null)
            {
                return Results.NotFound($"Document {request.DocumentId} was not found.");
            }

            await UpdateDocumentIngestionJobStatusAsync(options.SqlConnectionString, request.DocumentId, "Queued", 15, null);

            var workerPayload = new WorkerIngestRequest(request.DocumentId);
            try
            {
                using var httpClient = httpClientFactory.CreateClient();
                using var response = await httpClient.PostAsJsonAsync($"{options.WorkerDaprBaseUrl}/v1/ingest", workerPayload);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "Worker trigger returned HTTP {StatusCode} for document {DocumentId}. Job remains queued for polling worker.",
                        (int)response.StatusCode,
                        request.DocumentId);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Worker trigger call failed for document {DocumentId}. Job remains queued for polling worker.",
                    request.DocumentId);
            }

            return Results.Accepted($"/v1/uploads/{request.DocumentId}/status", new { request.DocumentId, status = "Queued" });
        });

        app.MapGet("/v1/uploads/{documentId:guid}/status", async (Guid documentId) =>
        {
            var status = await GetDocumentIngestionJobAsync(options.SqlConnectionString, documentId);
            return status is null ? Results.NotFound() : Results.Ok(status);
        });
    }

    private static async Task<string> CreateBlobUploadSasUrlAsync(
        BlobClient blobClient,
        DocumentIngestionOptions options,
        DateTimeOffset expiresAtUtc,
        IHttpClientFactory httpClientFactory)
    {
        var builder = new BlobSasBuilder
        {
            BlobContainerName = blobClient.BlobContainerName,
            BlobName = blobClient.Name,
            Resource = "b",
            ExpiresOn = expiresAtUtc
        };
        builder.SetPermissions(BlobSasPermissions.Create | BlobSasPermissions.Write | BlobSasPermissions.Add);

        if (string.Equals(options.StorageAuthMode, "managed-identity", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var accountName = ResolveStorageAccountName(options.StorageAccountName, options.StorageConnectionString);
                var accessToken = await AcquireManagedIdentityTokenAsync(
                    "https://storage.azure.com",
                    options.ManagedIdentityClientId,
                    httpClientFactory);
                var delegationKey = await GetUserDelegationKeyAsync(
                    accountName,
                    expiresAtUtc,
                    accessToken,
                    httpClientFactory);
                var sasWithDelegation = builder.ToSasQueryParameters(delegationKey, accountName).ToString();
                return $"{blobClient.Uri}?{sasWithDelegation}";
            }
            catch (HttpRequestException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (string.IsNullOrWhiteSpace(options.StorageConnectionString))
        {
            throw new InvalidOperationException(
                "Storage key fallback requires AZURE_STORAGE_CONNECTION_STRING when managed identity path is unavailable.");
        }

        var fallbackAccountName = GetStorageConnectionValue(options.StorageConnectionString, "AccountName");
        var accountKey = GetStorageConnectionValue(options.StorageConnectionString, "AccountKey");
        var credential = new StorageSharedKeyCredential(fallbackAccountName, accountKey);
        var sasToken = builder.ToSasQueryParameters(credential).ToString();
        return $"{blobClient.Uri}?{sasToken}";
    }

    private static string ResolveStorageAccountName(string configuredStorageAccountName, string connectionString)
    {
        if (!string.IsNullOrWhiteSpace(configuredStorageAccountName))
        {
            return configuredStorageAccountName;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Storage account name is required when connection string is not provided.");
        }

        return GetStorageConnectionValue(connectionString, "AccountName");
    }

    private static Uri BuildBlobUri(string accountName, string containerName, string blobName)
    {
        var escapedBlobPath = string.Join(
            "/",
            blobName.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        return new Uri($"https://{accountName}.blob.core.windows.net/{Uri.EscapeDataString(containerName)}/{escapedBlobPath}");
    }

    private static async Task<UserDelegationKey> GetUserDelegationKeyAsync(
        string accountName,
        DateTimeOffset expiresAtUtc,
        string bearerToken,
        IHttpClientFactory httpClientFactory)
    {
        var keyStart = DateTimeOffset.UtcNow;
        var keyInfoXml = $"""
                          <?xml version="1.0" encoding="utf-8"?>
                          <KeyInfo>
                            <Start>{keyStart.UtcDateTime:O}</Start>
                            <Expiry>{expiresAtUtc.UtcDateTime:O}</Expiry>
                          </KeyInfo>
                          """;

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://{accountName}.blob.core.windows.net/?restype=service&comp=userdelegationkey");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
        request.Headers.Add("x-ms-version", "2023-11-03");
        request.Content = new StringContent(keyInfoXml, Encoding.UTF8, "application/xml");

        using var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var responseXml = XDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = responseXml.Element("UserDelegationKey")
                   ?? throw new InvalidOperationException("Storage user delegation key response is invalid.");

        var signedObjectId = root.Element("SignedOid")?.Value
                             ?? throw new InvalidOperationException("SignedOid is missing in user delegation key response.");
        var signedTenantId = root.Element("SignedTid")?.Value
                             ?? throw new InvalidOperationException("SignedTid is missing in user delegation key response.");
        var signedStartsOn = DateTimeOffset.Parse(root.Element("SignedStart")?.Value
                                                  ?? throw new InvalidOperationException("SignedStart is missing in user delegation key response."));
        var signedExpiresOn = DateTimeOffset.Parse(root.Element("SignedExpiry")?.Value
                                                   ?? throw new InvalidOperationException("SignedExpiry is missing in user delegation key response."));
        var signedService = root.Element("SignedService")?.Value
                            ?? throw new InvalidOperationException("SignedService is missing in user delegation key response.");
        var signedVersion = root.Element("SignedVersion")?.Value
                            ?? throw new InvalidOperationException("SignedVersion is missing in user delegation key response.");
        var value = root.Element("Value")?.Value
                    ?? throw new InvalidOperationException("Value is missing in user delegation key response.");

        return BlobsModelFactory.UserDelegationKey(
            signedObjectId,
            signedTenantId,
            signedStartsOn,
            signedExpiresOn,
            signedService,
            signedVersion,
            value);
    }

    private static async Task<string> AcquireManagedIdentityTokenAsync(
        string resource,
        string? managedIdentityClientId,
        IHttpClientFactory httpClientFactory)
    {
        var identityEndpoint = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT");
        var identityHeader = Environment.GetEnvironmentVariable("IDENTITY_HEADER");
        if (string.IsNullOrWhiteSpace(identityEndpoint) || string.IsNullOrWhiteSpace(identityHeader))
        {
            throw new InvalidOperationException("Managed identity endpoint is not available in this environment.");
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
        using var responseJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!responseJson.RootElement.TryGetProperty("access_token", out var tokenElement))
        {
            throw new InvalidOperationException("Managed identity token response did not include access_token.");
        }

        return tokenElement.GetString()
               ?? throw new InvalidOperationException("Managed identity access_token was empty.");
    }

    private static string GetStorageConnectionValue(string connectionString, string key)
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

    private static async Task CreateDocumentIngestionJobAsync(
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

    private static async Task UpdateDocumentIngestionJobStatusAsync(
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

    private static async Task<DocumentIngestionStatus?> GetDocumentIngestionJobAsync(string connectionString, Guid documentId)
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
}

public sealed record DocumentIngestionOptions(
    string SqlConnectionString,
    string WorkerDaprBaseUrl,
    string StorageAccountName,
    string StorageConnectionString,
    string StorageContainerName,
    string StorageAuthMode,
    string? ManagedIdentityClientId,
    TimeSpan UploadUrlLifetime)
{
    public bool UploadConfigured =>
        (!string.IsNullOrWhiteSpace(StorageConnectionString) || !string.IsNullOrWhiteSpace(StorageAccountName)) &&
        !string.IsNullOrWhiteSpace(StorageContainerName);
}

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
