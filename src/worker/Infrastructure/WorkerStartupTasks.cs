using Microsoft.Data.SqlClient;

internal static class WorkerStartupTasks
{
    internal static async Task RequeueNonTerminalJobsAsync(string connectionString)
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

    internal static async Task EnsureWorkerSqlSchemaAsync(string connectionString)
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

    internal static async Task EnsureDatabaseExistsAsync(string connectionString)
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
}
