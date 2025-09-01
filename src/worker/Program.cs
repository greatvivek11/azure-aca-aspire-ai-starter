using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Add health checks
builder.Services.AddHealthChecks();

// Configure the application to listen on port 8081
builder.WebHost.UseUrls("http://*:8081");

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Health check endpoint
app.MapHealthChecks("/v1/health");

app.MapGet("/", () => "AI Hub Worker is running!");

app.Run();