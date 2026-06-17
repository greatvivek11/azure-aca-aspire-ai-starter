using Microsoft.Data.SqlClient;

namespace AcaAspireAiTemplate.Backend.Features.DocumentIngestion;

public interface IDocumentIngestionStore
{
    Task CreateOrUpdateJobAsync(
        Guid documentId,
        string fileName,
        string blobName,
        string status,
        int progressPercent);

    Task UpdateJobStatusAsync(
        Guid documentId,
        string status,
        int progressPercent,
        string? errorMessage,
        int? totalChunks = null,
        bool isReady = false);

    Task<DocumentIngestionStatus?> GetJobAsync(Guid documentId);
}

public sealed class SqlDocumentIngestionStore(string connectionString) : IDocumentIngestionStore
{
    public async Task CreateOrUpdateJobAsync(
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

    public async Task UpdateJobStatusAsync(
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

    public async Task<DocumentIngestionStatus?> GetJobAsync(Guid documentId)
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
