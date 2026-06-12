using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using AIHub.Backend.Infrastructure.Ai;

namespace AIHub.Backend.Features.AiPing;

public static class Endpoint
{
    public static void MapAiPingEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/ping-ai", async (IAiService aiService, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AIHub.Backend.Features.AiPing");

            try
            {
                var response = await aiService.InvokePromptAsync("Reply with exactly: Pong");
                return Results.Ok(new { response });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AI ping failed");
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