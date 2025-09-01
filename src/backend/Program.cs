using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Dapr.AspNetCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

var builder = WebApplication.CreateBuilder(args);

// Add Dapr support
builder.Services.AddDaprClient();

// Add health checks
builder.Services.AddHealthChecks();

// Add Semantic Kernel
builder.Services.AddKernel();
builder.Services.AddSingleton<Kernel>(sp =>
{
    var kernel = Kernel.CreateBuilder()
        .AddOpenAIChatCompletion(
            modelId: "gpt-3.5-turbo", // Placeholder model ID
            apiKey: Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "dummy-key")
        .Build();
    
    return kernel;
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Use Dapr
app.UseCloudEvents();
app.MapSubscribeHandler();

// Health check endpoint
app.MapHealthChecks("/v1/health");

// AI Ping endpoint
app.MapGet("/v1/ping-ai", async (Kernel kernel) =>
{
    try
    {
        // Invoke a simple prompt to test the connection
        var response = await kernel.InvokePromptAsync("Ping");
        return Results.Ok(new { response = response.ToString() });
    }
    catch
    {
        return Results.StatusCode(503);
    }
});

app.MapGet("/", () => "AI Hub Backend is running!");

app.Run();