using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Dapr.AspNetCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using AIHub.Backend.Features.Health;
using AIHub.Backend.Features.AiPing;
using AIHub.Backend.Infrastructure.Ai;

var builder = WebApplication.CreateBuilder(args);

// Add Dapr support
builder.Services.AddDaprClient();

// Add health checks
builder.Services.AddHealthChecks();

// Add Semantic Kernel
builder.Services.AddKernel();
builder.Services.AddSingleton<Kernel>(sp =>
{
    // Configure Semantic Kernel to use Hugging Face Inference Router
    var configuration = sp.GetRequiredService<IConfiguration>();
    
    // Prioritize environment variables over configuration files
    var huggingFaceApiKey = Environment.GetEnvironmentVariable("HUGGINGFACE_API_KEY") ?? configuration["HuggingFace:ApiKey"] ?? "dummy-key";
    var huggingFaceModelId = Environment.GetEnvironmentVariable("HUGGINGFACE_MODEL_ID") ?? configuration["HuggingFace:ModelId"] ?? "gpt2";
    var huggingFaceEndpoint = Environment.GetEnvironmentVariable("HUGGINGFACE_ENDPOINT") ?? configuration["HuggingFace:Endpoint"] ?? "https://api-inference.huggingface.co/v1/";
    
    Console.WriteLine($"Hugging Face Configuration:");
    Console.WriteLine($"API Key: {(!string.IsNullOrEmpty(huggingFaceApiKey) && huggingFaceApiKey != "dummy-key" ? "Set" : "Not set")}");
    Console.WriteLine($"Model ID: {huggingFaceModelId}");
    Console.WriteLine($"Endpoint: {huggingFaceEndpoint}");
    
    var kernel = Kernel.CreateBuilder()
        .AddOpenAIChatCompletion(
            modelId: huggingFaceModelId,
            apiKey: huggingFaceApiKey,
            endpoint: new Uri(huggingFaceEndpoint))
        .Build();
    
    return kernel;
});

// Register AI service
builder.Services.AddSingleton<IAiService, SemanticKernelService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Use Dapr
app.UseCloudEvents();
app.MapSubscribeHandler();

// Map feature endpoints
app.MapHealthEndpoint();
app.MapAiPingEndpoint();

app.MapGet("/", () => "AI Hub Backend is running!");

app.Run();