using Shouldly;
using Xunit;

namespace AcaAspireAiTemplate.Backend.Tests.Architecture;

public class WorkerArchitectureGuardTests
{
    private static readonly string RepositoryRoot = ResolveRepositoryRoot();

    [Fact]
    public void Worker_Should_Contain_Expected_Responsibility_Layered_Files()
    {
        var expectedPaths = new[]
        {
            "src/worker/Program.cs",
            "src/worker/Domain/DocumentIngestionJob.cs",
            "src/worker/Domain/SearchChunkDocument.cs",
            "src/worker/Domain/WorkerIngestRequest.cs",
            "src/worker/DocumentProcessing/DocumentProcessor.cs",
            "src/worker/DocumentProcessing/DocumentExtraction/TextExtractor.cs",
            "src/worker/DocumentProcessing/DocumentExtraction/TextChunker.cs",
            "src/worker/DocumentProcessing/Embedding/IEmbeddingService.cs",
            "src/worker/DocumentProcessing/Embedding/AzureOpenAiEmbeddingService.cs",
            "src/worker/DocumentProcessing/Embedding/LlamaCppEmbeddingService.cs",
            "src/worker/DocumentProcessing/Indexing/IVectorIndexer.cs",
            "src/worker/DocumentProcessing/Indexing/AzureSearchIndexer.cs",
            "src/worker/DocumentProcessing/Indexing/QdrantIndexer.cs",
            "src/worker/Infrastructure/Auth/AzureAuthenticator.cs",
            "src/worker/Infrastructure/Database/IDocumentJobRepository.cs",
            "src/worker/Infrastructure/Database/DocumentJobRepository.cs",
            "src/worker/Infrastructure/Storage/BlobStorageClient.cs",
            "src/worker/IngestionLoop/IngestionQueueProcessor.cs"
        };

        foreach (var relativePath in expectedPaths)
        {
            var absolutePath = Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            File.Exists(absolutePath).ShouldBeTrue($"Expected worker architecture file is missing: {relativePath}");
        }
    }

    [Fact]
    public void Worker_Program_Should_Remain_Thin_Composition_Root()
    {
        var programPath = Path.Combine(RepositoryRoot, "src", "worker", "Program.cs");
        File.Exists(programPath).ShouldBeTrue("Worker Program.cs was not found.");

        var source = File.ReadAllText(programPath);

        source.ShouldContain("AddSingleton<DocumentProcessor>()");
        source.ShouldContain("AddSingleton<IngestionQueueProcessor>()");
        source.ShouldContain("MapPost(\"/v1/ingest\"");

        source.ShouldNotContain("using Microsoft.Data.SqlClient;");
        source.ShouldNotContain("using DocumentFormat.OpenXml.Packaging;");
        source.ShouldNotContain("using UglyToad.PdfPig;");
        source.ShouldNotContain("SELECT DocumentId");
        source.ShouldNotContain("record DocumentIngestionJob");
        source.ShouldNotContain("class SearchChunkDocument");
    }

    [Fact]
    public void Worker_Domain_Should_Not_Depend_On_Infrastructure_Or_Azure_Sdks()
    {
        var domainFolder = Path.Combine(RepositoryRoot, "src", "worker", "Domain");
        Directory.Exists(domainFolder).ShouldBeTrue("Worker domain folder was not found.");

        var domainFiles = Directory.GetFiles(domainFolder, "*.cs", SearchOption.TopDirectoryOnly);
        domainFiles.Length.ShouldBeGreaterThan(0, "No worker domain files were found.");

        foreach (var file in domainFiles)
        {
            var source = File.ReadAllText(file);
            source.ShouldNotContain("using Azure");
            source.ShouldNotContain("using Microsoft.Data.SqlClient");
            source.ShouldNotContain("using Azure.Storage.Blobs");
            source.ShouldNotContain("using Azure.Search.Documents");
        }
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var slnPath = Path.Combine(directory.FullName, "azure-aca-aspire-ai-starter.sln");
            if (File.Exists(slnPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test execution directory.");
    }
}
