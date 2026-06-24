# Backend

The backend service for the ACA Aspire AI Starter — a .NET 10 Minimal API using Vertical Slice Architecture.

> See [docs/Architecture/Backend-Architecture.md](../../docs/Architecture/Backend-Architecture.md) for the full design.

## Structure

```
/src/backend/
├── Domain/                 # Core domain models (Document)
├── Features/               # Endpoint slices
│   ├── Health/             # GET /v1/health (anonymous)
│   ├── AiPing/             # GET /v1/ping-ai
│   ├── Chat/               # POST /v1/chat (general + RAG)
│   ├── Customers/          # /v1/customers CRUD
│   └── DocumentIngestion/  # /v1/uploads, /v1/ingest, status
├── Infrastructure/         # AI, Auth (Entra), Logging, Sql, Startup
├── Program.cs              # Composition root
└── Backend.csproj
```

## Endpoints

| Method | Route | Notes |
| --- | --- | --- |
| GET | `/v1/health` | Anonymous readiness check |
| GET | `/v1/ping-ai` | AI provider connectivity test |
| POST | `/v1/chat` | General chat or document-grounded RAG (`mode: "rag"`) |
| GET/POST/PUT/DELETE | `/v1/customers` | SQL-backed CRUD |
| POST | `/v1/uploads`, `/v1/uploads/signed-url` | Document upload to blob storage |
| POST | `/v1/ingest` | Queue a document for ingestion (via Dapr → worker) |
| GET | `/v1/uploads/{documentId}/status` | Ingestion status |

All feature endpoints except `/v1/health` require an Entra access token with the `access_as_user` scope.

## AI Provider

AI is abstracted behind `IAiService`, selected by `AI_MODE`:

- `AI_MODE=local` → `LlamaCppChatService` (local chat/embeddings + Qdrant vectors).
- `AI_MODE=azure` → `FoundryChatService` (Azure OpenAI chat/embeddings + Azure AI Search).

## Running

```bash
dotnet run                 # standalone
docker build -t api . && docker run -p 8080:8080 api
```

For local full-stack runs, prefer the Aspire host in `src/aspire` (it wires SQL, Redis, Azurite, Qdrant, and Dapr).

## Configuration

Configuration follows the standard .NET hierarchy (environment variables override `appsettings.json`). Key variables:

- **Azure mode**: `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_MODEL_ID`, `AZURE_OPENAI_AUTH_MODE` (`managed-identity` or `api-key`), `AZURE_OPENAI_API_KEY` (key mode only), `AZURE_SEARCH_ENDPOINT`, `AZURE_SEARCH_INDEX_NAME`.
- **Local mode**: `LLAMA_CPP_BASE_URL`, `LLAMA_CPP_EMBED_BASE_URL`, `LLAMA_CPP_CHAT_MODEL`, `LLAMA_CPP_EMBED_MODEL`, `QDRANT_URL`, `QDRANT_COLLECTION`.
- **Storage**: `AZURE_STORAGE_ACCOUNT_NAME`, `AZURE_STORAGE_CONNECTION_STRING`, `AZURE_STORAGE_CONTAINER_NAME`, `AZURE_STORAGE_AUTH_MODE`.
- **Auth**: `ENTRA_AUTH_ENABLED`, `ENTRA_TENANT_ID`, `ENTRA_API_CLIENT_ID`.

See the repo root [README](../../README.md) for the full environment-variable reference.
