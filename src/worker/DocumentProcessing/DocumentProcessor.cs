using System.Security.Cryptography;
using System.Text;

internal sealed class DocumentProcessor(
    WorkerRuntimeOptions runtimeOptions,
    IDocumentJobRepository jobRepository,
    BlobStorageClient blobStorageClient,
    TextExtractor textExtractor,
    TextChunker textChunker,
    AzureOpenAiEmbeddingService azureEmbeddingService,
    LlamaCppEmbeddingService localEmbeddingService,
    AzureSearchIndexer azureSearchIndexer,
    QdrantIndexer qdrantIndexer,
    ILogger<DocumentProcessor> logger)
{
    public async Task ProcessDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var job = await jobRepository.GetAsync(documentId, cancellationToken);
        if (job is null)
        {
            throw new InvalidOperationException($"Document {documentId} does not exist.");
        }

        await jobRepository.UpdateStatusAsync(documentId, "Extracting", 25, null, cancellationToken: cancellationToken);

        await using var blobStream = await blobStorageClient.OpenBlobReadStreamAsync(job.BlobName);
        var extractedText = await textExtractor.ExtractTextAsync(job.FileName, blobStream);
        if (string.IsNullOrWhiteSpace(extractedText))
        {
            throw new InvalidOperationException("No text content was extracted from the uploaded file.");
        }

        await jobRepository.UpdateStatusAsync(documentId, "Chunking", 45, null, cancellationToken: cancellationToken);
        var chunks = textChunker.ChunkText(extractedText, 220, 40);
        if (chunks.Count == 0)
        {
            throw new InvalidOperationException("Document text could not be chunked.");
        }

        await jobRepository.UpdateStatusAsync(documentId, "Embedding", 65, null, cancellationToken: cancellationToken);
        var searchDocuments = new List<SearchChunkDocument>();
        var embeddingService = string.Equals(runtimeOptions.AiMode, "local", StringComparison.OrdinalIgnoreCase)
            ? (IEmbeddingService)localEmbeddingService
            : azureEmbeddingService;

        for (var index = 0; index < chunks.Count; index++)
        {
            var chunk = chunks[index];
            var embedding = await embeddingService.GenerateEmbeddingAsync(chunk, cancellationToken);

            searchDocuments.Add(new SearchChunkDocument
            {
                Id = CreateQdrantPointId(documentId, index),
                DocumentId = documentId.ToString(),
                ChunkId = $"chunk-{index + 1}",
                FileName = job.FileName,
                Content = chunk,
                ContentVector = embedding
            });
        }

        await jobRepository.UpdateStatusAsync(documentId, "Indexing", 85, null, cancellationToken: cancellationToken);

        if (string.Equals(runtimeOptions.AiMode, "local", StringComparison.OrdinalIgnoreCase))
        {
            await qdrantIndexer.UpsertDocumentsAsync(searchDocuments, cancellationToken);
        }
        else
        {
            await azureSearchIndexer.UpsertDocumentsAsync(searchDocuments, cancellationToken);
        }

        await jobRepository.UpdateStatusAsync(
            documentId,
            "Ready",
            100,
            null,
            chunks.Count,
            true,
            cancellationToken);

        logger.LogInformation(
            "Document {DocumentId} ingestion completed successfully. Chunks indexed: {ChunkCount}",
            documentId,
            chunks.Count);
    }

    private static string CreateQdrantPointId(Guid documentId, int chunkIndex)
    {
        var source = $"{documentId:N}:{chunkIndex:D5}";
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(source));
        return new Guid(bytes).ToString("D");
    }
}
