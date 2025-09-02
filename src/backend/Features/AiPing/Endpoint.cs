using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.SemanticKernel;

namespace AIHub.Backend.Features.AiPing;

public static class Endpoint
{
    public static void MapAiPingEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/ping-ai", async (Kernel kernel) =>
        {
            try
            {
                // Invoke a simple prompt to test the connection
                var response = await kernel.InvokePromptAsync("Ping");
                return Results.Ok(new { response = response.ToString() });
            }
            catch (Exception ex)
            {
                // Log the exception details
                Console.WriteLine($"AI Ping failed: {ex.Message}");
                Console.WriteLine($"Exception details: {ex}");
                return Results.StatusCode(503);
            }
        })
        .WithName("AiPing")
        .WithTags("AI")
        .Produces<AiPingResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status503ServiceUnavailable);
    }
}

public class AiPingResponse
{
    public string Response { get; set; } = string.Empty;
}