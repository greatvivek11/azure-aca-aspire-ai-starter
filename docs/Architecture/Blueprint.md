# Technical Blueprint

The intended system design for the Azure ACA Aspire AI Starter: a minimal, reusable, cloud-native template for enterprise AI apps built with .NET, React, and Azure.

> Source of truth: live code and manifests (`azure.yaml`, `infra/`, `src/*`). Where this document and code diverge, follow the code.

## Vision

Provide a production-aligned starter that combines conversational AI and document retrieval (RAG) with fast local setup and low operational complexity. The template is scoped to free-tier-friendly deployments rather than full enterprise hardening, but uses enterprise-shaped patterns (Entra auth, managed identity, private backend, IaC, CI/CD).

## What It Does

- Chat with an AI assistant grounded in uploaded documents (RAG with citations).
- Upload and ingest documents (`.txt`, `.pdf`, `.docx`) to blob storage.
- Manage relational records (a Customers CRUD slice demonstrates SQL access).

> Vision/OCR, sentiment analysis, and long-term memory are aspirational, not implemented in the current template.

## Architecture & Stack

| Layer | Technology |
| --- | --- |
| Frontend | React SPA (Vite) served by a lightweight Hono Node host; MSAL/Entra auth; npm |
| Backend | .NET 10 Minimal APIs, Vertical Slice Architecture, Dapr-enabled |
| Worker | .NET background worker for document ingestion/embeddings |
| Orchestration | .NET Aspire (`src/aspire/AppHost.cs`) for local composition, service discovery, telemetry |
| Service comms | Dapr service invocation (FE → Dapr → BE); worker triggered via Dapr |
| AI (azure mode) | Azure AI Foundry / Azure OpenAI for chat + embeddings; Azure AI Search for vectors |
| AI (local mode) | Ollama for chat + embeddings; Qdrant for vectors; Azurite for blob |
| Data | Azure SQL (relational), Azure Blob Storage (files), vector store (AI Search or Qdrant) |
| Identity | Microsoft Entra ID auth; passwordless User-Assigned Managed Identity for Azure services |
| IaC / CI-CD | Bicep (`infra/`) deployed via GitHub Actions + `azd provision`/`azd deploy` |
| Observability | OpenTelemetry from Aspire; optional Log Analytics + Application Insights |

The AI provider is abstracted behind `IAiService` (`OllamaChatService` for local, `FoundryChatService` for azure), selected by the `AI_MODE` environment variable.

## Principles

- **Vertical slices**: features implemented end-to-end and kept independent (no cross-feature coupling).
- **Single backend project**: orchestrated by Aspire; extract slices as the surface grows rather than adding layers prematurely.
- **Dapr-first communication**: frontend invokes backend via the Dapr sidecar.
- **Cloud-native**: stateless services with externalized state in SQL, Blob, and the vector store.
- **Cost-aware defaults**: consumption plan, scale-to-zero where possible, free/low SKUs, opt-in telemetry.
- **Provider-swappable AI**: the `IAiService` abstraction isolates chat/embedding providers.

## Repository Structure

```
/
├── src/
│   ├── frontend/   # Vite React SPA served by a Hono Node host
│   ├── backend/    # .NET Minimal API (Vertical Slice Architecture)
│   ├── worker/     # .NET background worker (ingestion/embeddings)
│   └── aspire/     # AppHost for local orchestration
├── infra/          # Bicep IaC for ACA, SQL, Storage, AI Search, AI Foundry
├── docs/           # Architecture and operational docs
└── .github/        # CI/CD workflows (validate → provision → deploy)
```

## AI Modes

- **`AI_MODE=local`** (default for local dev): Ollama + Qdrant + Azurite run as containers via Aspire. No Azure resources required.
- **`AI_MODE=azure`** (default for cloud deploy): Azure OpenAI/Foundry + Azure AI Search + Azure Blob Storage. Local mode is not deployable to cloud as-is.

## Constraints

- Free tiers, consumption plans, and minimal SKUs by default.
- Secrets via GitHub Actions (no Key Vault in the default profile).
- No Private Endpoints or Defender for Cloud in the baseline — see [Network Hardening Extension](./Network-Hardening-Extension.md) for the optional path.

## Related Docs

- [Cloud Architecture](./Cloud-Architecture.md) — end-to-end deployment and security.
- [Backend Architecture](./Backend-Architecture.md) — backend structure and patterns.
- [Frontend Architecture](./Frontend-Architecture.md) — frontend stack and patterns.
- [Network Hardening Extension](./Network-Hardening-Extension.md) — optional VNET/private-endpoint hardening.
