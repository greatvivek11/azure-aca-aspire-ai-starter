using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using OpenTelemetry;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Dapr.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add Dapr support
builder.Services.AddDaprClient();

// Send logs, traces, and metrics to Application Insights when configured.
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

// Add health checks
builder.Services.AddHealthChecks();

// Configure the application to listen on port 8081
builder.WebHost.UseUrls("http://*:8081");

var app = builder.Build();

app.Logger.LogInformation("AI Hub worker started on port 8081");
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

// Health check endpoint
app.MapHealthChecks("/v1/health");

app.MapGet("/", () => "AI Hub Worker is running!");

app.Run();