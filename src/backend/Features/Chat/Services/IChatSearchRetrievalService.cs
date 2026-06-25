namespace AcaAspireAiTemplate.Backend.Features.Chat;

internal interface IChatSearchRetrievalService
{
    Task<List<SearchChunkDocument>> SearchRelevantChunksAsync(
        float[] embedding,
        Guid? documentId,
        CancellationToken cancellationToken = default);
}
