using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using OpenTelemetry;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Dapr.AspNetCore;
using Microsoft.Data.SqlClient;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using AIHub.Backend.Features.Health;
using AIHub.Backend.Features.AiPing;
using AIHub.Backend.Infrastructure.Ai;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// Add Dapr support
builder.Services.AddDaprClient();
// Wire structured logs, traces, and metrics via OpenTelemetry.
// In Aspire, OTEL_EXPORTER_OTLP_ENDPOINT is injected by the AppHost so logs/traces/metrics
// appear in the local dashboard. APPLICATIONINSIGHTS_CONNECTION_STRING enables Azure Monitor
// export automatically when deployed to Azure.
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

// Add health checks
builder.Services.AddHealthChecks();

// Register configuration options
builder.Services.Configure<AIHub.Backend.Infrastructure.Ai.AzureOpenAiOptions>(
    builder.Configuration.GetSection(AIHub.Backend.Infrastructure.Ai.AzureOpenAiOptions.SectionName));

// Validate Azure OpenAI settings during startup so misconfiguration fails fast.
var azureOpenAiSettings = ResolveAzureOpenAiSettings(builder.Configuration);
Console.WriteLine("Azure OpenAI Configuration:");
Console.WriteLine("API Key: Set");
Console.WriteLine($"Model ID: {azureOpenAiSettings.ModelId}");
Console.WriteLine($"Endpoint: {azureOpenAiSettings.Endpoint}");

// Add Semantic Kernel
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

// Register AI service
builder.Services.AddSingleton<IAiService, SemanticKernelService>();

var app = builder.Build();
var sqlConnectionString = GetSqlConnectionString(app.Configuration);
await EnsureSqlSchemaAsync(sqlConnectionString);

app.Logger.LogInformation(
    "Azure OpenAI configuration loaded. ModelId={ModelId} Endpoint={Endpoint}",
    azureOpenAiSettings.ModelId,
    azureOpenAiSettings.Endpoint);
if (string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    app.Logger.LogInformation(
        "Application Insights connection string not set. Azure Monitor exporter is disabled for this run.");
}

// Configure the HTTP request pipeline
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

// Use Dapr
app.UseCloudEvents();
app.MapSubscribeHandler();

// Map feature endpoints
app.MapHealthEndpoint();
app.MapAiPingEndpoint();

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

static AzureOpenAiSettings ResolveAzureOpenAiSettings(IConfiguration configuration)
{
    var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
        ?? configuration[$"{AzureOpenAiOptions.SectionName}:ApiKey"];
    var modelId = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_ID")
        ?? configuration[$"{AzureOpenAiOptions.SectionName}:ModelId"];
    var endpointText = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
        ?? configuration[$"{AzureOpenAiOptions.SectionName}:Endpoint"];

    var issues = new List<string>();

    if (string.IsNullOrWhiteSpace(apiKey))
    {
        issues.Add("AZURE_OPENAI_API_KEY is missing");
    }

    if (string.IsNullOrWhiteSpace(modelId))
    {
        issues.Add("AZURE_OPENAI_MODEL_ID is missing");
    }

    if (string.IsNullOrWhiteSpace(endpointText))
    {
        issues.Add("AZURE_OPENAI_ENDPOINT is missing");
    }

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

    return new AzureOpenAiSettings(
        apiKey!,
        modelId!,
        new Uri(endpointText!, UriKind.Absolute));
}

static string GetSqlConnectionString(IConfiguration configuration)
{
    // Prefer explicit connection string (local Aspire dev with SQL password).
    var explicitConnectionString = configuration.GetConnectionString("SqlServer")
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__SqlServer");

    if (!string.IsNullOrWhiteSpace(explicitConnectionString))
    {
        return explicitConnectionString;
    }

    // Production path: build connection string using Managed Identity.
    // SQL_SERVER and SQL_DATABASE are injected by Bicep as plain env vars.
    // AZURE_CLIENT_ID is the UAMI client ID, also injected by Bicep.
    // SqlClient v5+ handles token acquisition automatically when
    // Authentication=Active Directory Managed Identity is set.
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
            $"SQL configuration is incomplete. Missing: {string.Join(", ", missing)}. "
            + "Set ConnectionStrings:SqlServer for local dev, or SQL_SERVER + SQL_DATABASE + AZURE_CLIENT_ID for Azure.");
    }

    return $"Server=tcp:{sqlServer},1433;Initial Catalog={sqlDatabase};"
         + $"Authentication=Active Directory Managed Identity;User Id={uamiClientId};"
         + "Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";
}

static async Task EnsureSqlSchemaAsync(string connectionString)
{
    var connectionBuilder = new SqlConnectionStringBuilder(connectionString);
    var databaseName = connectionBuilder.InitialCatalog;

    if (string.IsNullOrWhiteSpace(databaseName))
    {
        throw new InvalidOperationException("SQL connection string must include a database name.");
    }

    // Database is already provisioned by Bicep IaC during infrastructure deployment.
    // Skip CREATE DATABASE and connect directly to seed tables/schema.
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    var seedScriptPath = Path.Combine(AppContext.BaseDirectory, "Infrastructure", "Sql", "seed.sql");
    var seedScript = await File.ReadAllTextAsync(seedScriptPath);

    await using var seedCommand = connection.CreateCommand();
    seedCommand.CommandText = seedScript;
    await seedCommand.ExecuteNonQueryAsync();
}

static async Task<List<CustomerRecord>> GetCustomersAsync(string connectionString)
{
    var customers = new List<CustomerRecord>();
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = @"
SELECT Id, Name, Email, City, Status
FROM dbo.Customers
ORDER BY Id;";

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
    command.CommandText = @"
INSERT INTO dbo.Customers (Name, Email, City, Status)
OUTPUT INSERTED.Id
VALUES (@name, @email, @city, @status);";
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
    command.CommandText = @"
UPDATE dbo.Customers
SET Name = @name,
    Email = @email,
    City = @city,
    Status = @status
WHERE Id = @id;";
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