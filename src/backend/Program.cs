using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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

// Register configuration options
builder.Services.Configure<AIHub.Backend.Infrastructure.Ai.HuggingFaceOptions>(
    builder.Configuration.GetSection(AIHub.Backend.Infrastructure.Ai.HuggingFaceOptions.SectionName));

// Add Semantic Kernel
builder.Services.AddKernel();
builder.Services.AddSingleton<Kernel>(sp =>
{
    // Configure Semantic Kernel to use Hugging Face Inference Router
    var options = sp.GetRequiredService<IOptions<AIHub.Backend.Infrastructure.Ai.HuggingFaceOptions>>().Value;
    
    // Prioritize environment variables over configuration options
    var huggingFaceApiKey = Environment.GetEnvironmentVariable("HUGGINGFACE_API_KEY") ?? options.ApiKey;
    var huggingFaceModelId = Environment.GetEnvironmentVariable("HUGGINGFACE_MODEL_ID") ?? options.ModelId;
    var huggingFaceEndpoint = Environment.GetEnvironmentVariable("HUGGINGFACE_ENDPOINT") ?? options.Endpoint;
    
    Console.WriteLine($"Hugging Face Configuration:");
    Console.WriteLine($"API Key: {(!string.IsNullOrEmpty(huggingFaceApiKey) ? "Set" : "Not set")}");
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