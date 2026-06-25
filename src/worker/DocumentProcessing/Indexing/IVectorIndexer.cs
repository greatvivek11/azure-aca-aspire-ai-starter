internal interface IVectorIndexer
{
    Task UpsertDocumentsAsync(IReadOnlyCollection<SearchChunkDocument> documents, CancellationToken cancellationToken = default);
}
