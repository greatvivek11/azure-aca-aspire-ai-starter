using Azure.Storage.Blobs;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AcaAspireAiTemplate.Backend.Infrastructure.Startup;

public static class BackendStartupTasks
{
    public static async Task EnsureSqlSchemaAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var seedScriptPath = Path.Combine(AppContext.BaseDirectory, "Infrastructure", "Sql", "seed.sql");
        var seedScript = await File.ReadAllTextAsync(seedScriptPath);

        await using var seedCommand = connection.CreateCommand();
        seedCommand.CommandText = seedScript;
        await seedCommand.ExecuteNonQueryAsync();
    }

    public static async Task EnsureDatabaseExistsAsync(string connectionString)
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

    public static async Task EnsureLocalBlobStorageAsync(
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

    public static async Task RunStartupStepAsync(string stepName, Func<Task> action, ILogger logger)
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
}
