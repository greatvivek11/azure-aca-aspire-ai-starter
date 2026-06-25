using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;

internal sealed class AzureSearchIndexer(WorkerRuntimeOptions runtimeOptions) : IVectorIndexer
{
    public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        var indexClient = new SearchIndexClient(new Uri(runtimeOptions.SearchEndpoint), new AzureKeyCredential(runtimeOptions.SearchApiKey));

        try
        {
            await indexClient.GetIndexAsync(runtimeOptions.SearchIndexName, cancellationToken);
            return;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }

        var fields = new List<SearchField>
        {
            new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
            new SimpleField("documentId", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
            new SimpleField("chunkId", SearchFieldDataType.String) { IsFilterable = true },
            new SearchableField("fileName") { IsFilterable = true, IsSortable = true },
            new SearchableField("content"),
            new SearchField("contentVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                IsSearchable = true,
                VectorSearchDimensions = runtimeOptions.EmbeddingDimensions,
                VectorSearchProfileName = "vector-profile"
            }
        };

        var index = new SearchIndex(runtimeOptions.SearchIndexName, fields)
        {
            VectorSearch = new VectorSearch
            {
                Algorithms = { new HnswAlgorithmConfiguration("hnsw-default") },
                Profiles = { new VectorSearchProfile("vector-profile", "hnsw-default") }
            }
        };

        await indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: cancellationToken);
    }

    public async Task UpsertDocumentsAsync(IReadOnlyCollection<SearchChunkDocument> documents, CancellationToken cancellationToken = default)
    {
        var searchClient = new SearchClient(
            new Uri(runtimeOptions.SearchEndpoint),
            runtimeOptions.SearchIndexName,
            new AzureKeyCredential(runtimeOptions.SearchApiKey));

        await searchClient.MergeOrUploadDocumentsAsync(documents, cancellationToken: cancellationToken);
    }
}
