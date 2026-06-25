namespace AcaAspireAiTemplate.Backend.Features.Chat;

internal sealed record ChatRequest(string Message, string? Mode, Guid? DocumentId);
internal sealed record ChatCitation(string DocumentId, string ChunkId, string FileName);
internal sealed record ChatResponse(string Answer, IReadOnlyList<ChatCitation> Citations);

internal sealed class SearchChunkDocument
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string ChunkId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
