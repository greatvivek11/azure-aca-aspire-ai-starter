# Worker Architecture

## Why Worker Uses Responsibility Layering

The backend uses Vertical Slice Architecture because it exposes multiple independent API feature areas. The worker is different: it is a single-purpose background service that repeatedly executes one ingestion workflow. For this style of service, a responsibility-layered structure is easier to reason about and test.

## Structure

```text
src/worker/
├── Program.cs                          (startup, DI, endpoints)
├── Domain/                             (core records and models)
├── DocumentProcessing/                 (pipeline orchestration and steps)
│   ├── DocumentProcessor.cs
│   ├── DocumentExtraction/
│   │   ├── TextExtractor.cs
│   │   └── TextChunker.cs
│   ├── Embedding/
│   │   ├── IEmbeddingService.cs
│   │   ├── AzureOpenAiEmbeddingService.cs
│   │   └── LlamaCppEmbeddingService.cs
│   └── Indexing/
│       ├── IVectorIndexer.cs
│       ├── AzureSearchIndexer.cs
│       └── QdrantIndexer.cs
├── Infrastructure/
│   ├── Auth/
│   │   └── AzureAuthenticator.cs
│   ├── Database/
│   │   ├── IDocumentJobRepository.cs
│   │   └── DocumentJobRepository.cs
│   └── Storage/
│       └── BlobStorageClient.cs
└── IngestionLoop/
    └── IngestionQueueProcessor.cs
```

## Design Principles

- Keep each class focused on one reason to change.
- Prefer interface-based boundaries where behavior may vary by runtime mode.
- Keep domain models free of infrastructure dependencies.
- Keep retry and fallback rules close to the external integration they protect.
- Keep Program.cs thin and focused on composition.

## Testing Guidance

- Unit test `DocumentProcessor` with mocked collaborators.
- Unit test embedding services for retry, auth fallback, and error paths.
- Unit test `IngestionQueueProcessor` loop behavior with simulated transient and non-transient failures.
- Keep integration tests for SQL and indexing adapters at infrastructure boundaries.
