using AcaAspireAiTemplate.Backend.Infrastructure.Ai;

namespace AcaAspireAiTemplate.Backend.Features.Chat;

internal sealed class ChatRagOrchestrator(
    ChatOptions options,
    IAiService aiService,
    IChatEmbeddingService embeddingService,
    IChatSearchRetrievalService retrievalService)
{
    public async Task<ChatResponse> GenerateDocsResponseAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var embedding = await embeddingService.GenerateEmbeddingAsync(request.Message, cancellationToken);
        var searchResults = await retrievalService.SearchRelevantChunksAsync(embedding, request.DocumentId, cancellationToken);

        var citations = new List<ChatCitation>();
        var contextParts = new List<string>();
        var contextCharacterCount = 0;

        foreach (var result in searchResults)
        {
            if (string.IsNullOrWhiteSpace(result.Content))
            {
                continue;
            }

            var context = TrimForLocalRag(result.Content, options.LocalRagMaxChunkCharacters);
            if (options.IsLocalMode && contextCharacterCount + context.Length > options.LocalRagMaxContextCharacters)
            {
                var remainingCharacters = options.LocalRagMaxContextCharacters - contextCharacterCount;
                if (remainingCharacters <= 0)
                {
                    break;
                }

                context = TrimForLocalRag(context, remainingCharacters);
            }

            contextParts.Add(context);
            contextCharacterCount += context.Length;
            citations.Add(new ChatCitation(
                result.DocumentId,
                result.ChunkId,
                result.FileName));
        }

        if (contextParts.Count == 0)
        {
            return new ChatResponse(
                "I could not find relevant indexed content for this question yet. Upload and ingest a file first.",
                citations);
        }

        if (options.IsLocalMode && options.LocalRagFastResponse)
        {
            return new ChatResponse(BuildLocalRagFastResponse(contextParts), citations);
        }

        var prompt = BuildRagPrompt(request.Message, contextParts);
        var answer = await aiService.InvokePromptAsync(prompt);
        return new ChatResponse(answer, citations);
    }

    private static string BuildRagPrompt(string question, IEnumerable<string> contexts)
    {
        var contextBlock = string.Join("\n\n---\n\n", contexts);
        return $"""
You are an enterprise copilot assistant.
    Answer only from the provided context. If context is insufficient, say so. Keep the answer concise.

Context:
{contextBlock}

Question:
{question}
""";
    }

    private static string BuildLocalRagFastResponse(IEnumerable<string> contexts)
    {
        var snippets = contexts
            .Select(context => TrimForLocalRag(context, 450))
            .Where(context => !string.IsNullOrWhiteSpace(context))
            .Take(3)
            .ToArray();

        if (snippets.Length == 0)
        {
            return "I found indexed content for the document, but it did not include enough readable text to summarize.";
        }

        return "Based on the indexed document:\n\n" + string.Join("\n\n", snippets.Select(snippet => $"- {snippet}"));
    }

    private static string TrimForLocalRag(string value, int maxCharacters)
    {
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length <= maxCharacters)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, maxCharacters - 3)].TrimEnd() + "...";
    }
}
