using Microsoft.Data.SqlClient;

internal sealed class DocumentJobRepository(WorkerRuntimeOptions runtimeOptions) : IDocumentJobRepository
{
    public async Task<DocumentIngestionJob?> GetAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(runtimeOptions.SqlConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT DocumentId, FileName, BlobName, Status
FROM dbo.DocumentIngestionJobs
WHERE DocumentId = @documentId;
""";
        command.Parameters.AddWithValue("@documentId", documentId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new DocumentIngestionJob(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3));
    }

    public async Task UpdateStatusAsync(
        Guid documentId,
        string status,
        int progressPercent,
        string? errorMessage,
        int? totalChunks = null,
        bool isReady = false,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(runtimeOptions.SqlConnectionString);
        await connection.OpenAsync(cancellationToken);

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
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DocumentIngestionJob?> TryClaimNextQueuedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(runtimeOptions.SqlConnectionString);
        await connection.OpenAsync(cancellationToken);

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

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new DocumentIngestionJob(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3));
    }
}
