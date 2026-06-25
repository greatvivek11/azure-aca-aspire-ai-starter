using AcaAspireAiTemplate.Backend.Infrastructure.Auth;
using AcaAspireAiTemplate.Backend.Infrastructure.Ai;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AcaAspireAiTemplate.Backend.Features.Chat;

public static class Endpoint
{
    public static void MapChatEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/chat", async (ChatRequest request, IAiService aiService, ChatOptions options, ChatRagOrchestrator ragOrchestrator) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return Results.BadRequest("message is required.");
            }

            var mode = (request.Mode ?? "general").Trim().ToLowerInvariant();
            if (mode is "docs" or "rag")
            {
                if (!options.SearchConfigured)
                {
                    return Results.BadRequest("RAG mode is not configured. Missing search endpoint/index or usable search authentication.");
                }

                var ragResponse = await ragOrchestrator.GenerateDocsResponseAsync(request);
                return Results.Ok(ragResponse);
            }

            var generalAnswer = await aiService.InvokePromptAsync(request.Message);
            return Results.Ok(new ChatResponse(generalAnswer, []));
        }).RequireAuthorization(EntraAuthSetup.ApiScopePolicyName);
    }
}
