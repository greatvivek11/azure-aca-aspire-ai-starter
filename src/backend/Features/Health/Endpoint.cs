using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AcaAspireAiTemplate.Backend.Features.Health;

public static class Endpoint
{
    public static void MapHealthEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/health", () =>
        {
            return Results.Ok(new { status = "Healthy" });
        })
        .AllowAnonymous()
        .WithName("HealthCheck")
        .WithTags("Health")
        .Produces<HealthResponse>(StatusCodes.Status200OK);
    }
}

public class HealthResponse
{
    public string Status { get; set; } = string.Empty;
}