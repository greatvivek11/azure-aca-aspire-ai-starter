using System.Text;
using System.Text.Json;

internal sealed class QdrantIndexer(
    WorkerRuntimeOptions runtimeOptions,
    IHttpClientFactory httpClientFactory) : IVectorIndexer
{
    public async Task EnsureCollectionAsync(ILogger logger, CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient();
        using var response = await CreateCollectionAsync(
            client,
            runtimeOptions.QdrantUrl,
            runtimeOptions.QdrantCollection,
            runtimeOptions.EmbeddingDimensions,
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var existingDimensions = await TryGetCollectionSizeAsync(
                client,
                runtimeOptions.QdrantUrl,
                runtimeOptions.QdrantCollection,
                cancellationToken);

            if (existingDimensions.HasValue && existingDimensions.Value != runtimeOptions.EmbeddingDimensions)
            {
                logger.LogWarning(
                    "Qdrant collection dimensions mismatch detected. Recreating collection. Collection={Collection}, ExistingDimensions={ExistingDimensions}, ExpectedDimensions={ExpectedDimensions}",
                    runtimeOptions.QdrantCollection,
                    existingDimensions.Value,
                    runtimeOptions.EmbeddingDimensions);

                using var deleteResponse = await client.DeleteAsync(
                    $"{runtimeOptions.QdrantUrl.TrimEnd('/')}/collections/{runtimeOptions.QdrantCollection}",
                    cancellationToken);
                var deleteResponseBody = await deleteResponse.Content.ReadAsStringAsync(cancellationToken);
                if (!deleteResponse.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"Failed to delete mismatched Qdrant collection '{runtimeOptions.QdrantCollection}'. Status={(int)deleteResponse.StatusCode}. Body={deleteResponseBody}");
                }

                using var recreateResponse = await CreateCollectionAsync(
                    client,
                    runtimeOptions.QdrantUrl,
                    runtimeOptions.QdrantCollection,
                    runtimeOptions.EmbeddingDimensions,
                    cancellationToken);
                var recreateResponseBody = await recreateResponse.Content.ReadAsStringAsync(cancellationToken);
                if (!recreateResponse.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"Failed to recreate Qdrant collection '{runtimeOptions.QdrantCollection}'. Status={(int)recreateResponse.StatusCode}. Body={recreateResponseBody}");
                }

                logger.LogInformation(
                    "Recreated Qdrant collection with expected dimensions. Collection={Collection}, Dimensions={Dimensions}",
                    runtimeOptions.QdrantCollection,
                    runtimeOptions.EmbeddingDimensions);
                return;
            }

            logger.LogInformation(
                "Qdrant collection already exists; continuing startup. Collection={Collection}, Response={ResponseBody}",
                runtimeOptions.QdrantCollection,
                responseBody);
            return;
        }

        throw new HttpRequestException(
            $"Failed to ensure Qdrant collection '{runtimeOptions.QdrantCollection}'. Status={(int)response.StatusCode}. Body={responseBody}");
    }

    public async Task UpsertDocumentsAsync(IReadOnlyCollection<SearchChunkDocument> documents, CancellationToken cancellationToken = default)
    {
        var points = documents.Select(document => new
        {
            id = document.Id,
            vector = document.ContentVector,
            payload = new
            {
                id = document.Id,
                documentId = document.DocumentId,
                chunkId = document.ChunkId,
                fileName = document.FileName,
                content = document.Content
            }
        });

        using var payload = new StringContent(
            JsonSerializer.Serialize(new { points }),
            Encoding.UTF8,
            "application/json");

        using var client = httpClientFactory.CreateClient();
        using var response = await client.PutAsync(
            $"{runtimeOptions.QdrantUrl.TrimEnd('/')}/collections/{runtimeOptions.QdrantCollection}/points?wait=true",
            payload,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Failed to upsert Qdrant documents for collection '{runtimeOptions.QdrantCollection}'. Status={(int)response.StatusCode}. Body={responseBody}");
        }
    }

    private static Task<HttpResponseMessage> CreateCollectionAsync(
        HttpClient client,
        string qdrantUrl,
        string collectionName,
        int embeddingDimensions,
        CancellationToken cancellationToken)
    {
        var payload = new StringContent(
            JsonSerializer.Serialize(new
            {
                vectors = new
                {
                    size = embeddingDimensions,
                    distance = "Cosine"
                }
            }),
            Encoding.UTF8,
            "application/json");

        return client.PutAsync(
            $"{qdrantUrl.TrimEnd('/')}/collections/{collectionName}",
            payload,
            cancellationToken);
    }

    private static async Task<int?> TryGetCollectionSizeAsync(
        HttpClient client,
        string qdrantUrl,
        string collectionName,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            $"{qdrantUrl.TrimEnd('/')}/collections/{collectionName}",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("config", out var config)
            || !config.TryGetProperty("params", out var parameters)
            || !parameters.TryGetProperty("vectors", out var vectors)
            || !vectors.TryGetProperty("size", out var sizeElement))
        {
            return null;
        }

        return sizeElement.GetInt32();
    }
}
