internal interface IDocumentJobRepository
{
    Task<DocumentIngestionJob?> GetAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<DocumentIngestionJob?> TryClaimNextQueuedAsync(CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(
        Guid documentId,
        string status,
        int progressPercent,
        string? errorMessage,
        int? totalChunks = null,
        bool isReady = false,
        CancellationToken cancellationToken = default);
}
