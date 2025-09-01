# 🛠️ Technical Blueprint: Cloud-Native AI Hub

**A detailed guide for building the AI Knowledge Hub on Azure, using Dapr, Cosmos DB, and the Hugging Face Inference Router.**

---

### 📚 Key Facts & References

* **Semantic Kernel**: Microsoft’s open-source SDK for AI orchestration. [GitHub](https://github.com/microsoft/semantic-kernel) | [Docs](https://learn.microsoft.com/en-us/semantic-kernel/overview/)
* **Azure Container Apps**: Managed environment for running microservices and containerized applications. [Docs](https://learn.microsoft.com/en-us/azure/container-apps/)
* **Dapr**: APIs for building resilient, stateful, and event-driven distributed applications. [Docs](https://docs.dapr.io/)
* **Azure Cosmos DB for MongoDB (vCore)**: A powerful, scalable NoSQL database with integrated vector search capabilities. [Vector Search Docs](https://learn.microsoft.com/en-us/azure/cosmos-db/mongodb/vcore/vector-search)
* **Hugging Face Router**: A proxy for routing requests to various open-source models. [Website](https://huggingface.co/inference-endpoints/router)

## 📋 A. Architecture & Technology Stack

* **Frontend**: React 19 + **Next.js (SSR/BFF)** + Vite 7 + Tailwind v4 + shadcn/ui. (Alternatives for SSR: Remix, SolidStart.)
* **Backend**: .NET 8 Minimal APIs, **.NET Aspire** (AppHost & service discovery), Vertical Slice Architecture, Mediator (source-gen)
* **Infra**: Azure Container Apps (FE public, BE internal), Dapr sidecars for service invocation (and later pub/sub for ingestion).
* **Service Communication**: Dapr service invocation (FE → Dapr → BE); optional Dapr pub/sub for long-running ingestion/OCR in Phase 2+.
* **Data**:

  * Azure SQL Database (relational data)
  * Azure Blob Storage (file storage)
  * Azure Cosmos DB (vector embeddings & memory)
* **AI Orchestration**:

  * Semantic Kernel
  * Hugging Face Inference Router via SK OpenAI connector
* **IaC**: Bicep/Terraform scripts to provision ACA, SQL, Blob, Cosmos, network.
* **Deployment**: GitHub Actions → **GHCR** → ACA (ACR also supported if desired).
* **Observability**: ACA logs, optional App Insights + Log Analytics (free-tier friendly via sampling).

## B. Phased Implementation Plan

See: [Phased-Plan.md](/docs/plans/Phased_Plan.md).

## C. Architecture Principles

* **Vertical Slice**: Features implemented end-to-end (API → Domain → Infra).
* **Mediator**: Source-generated mediator pattern for clean command/query separation.
* **Dapr-first**: FE invokes BE via Dapr sidecar app-id.
* **Cloud-native**: Stateless services, externalized state in SQL/Blob/Cosmos.
* **Cost-aware**: Free-tier defaults, enterprise add-ons only documented.
* **Extensible**: AI provider abstracted (SK service layer) to swap providers easily.
* **SSR/BFF**: Server-only routes keep secrets on the server; streaming SSR improves FCP and user-perceived latency.
* **Aspire Orchestration**: Local composition of services with health/telemetry baked in.

## D. Repository Structure

```
/
 ├── frontend/         # Next.js (React 19) SSR app, route handlers (BFF)
 ├── backend/          # .NET Minimal API (VSA), SK orchestration
 ├── worker/           # .NET background worker (ingest/embeddings/OCR)
 ├── aspire/           # AppHost & Aspire configuration for local orchestration
 ├── infra/            # Bicep/Terraform IaC for ACA, SQL, Blob, Cosmos, Dapr
 ├── docs/             # Background, Phased-Plan, Blueprint, Architecture, Decisions
 └── .github/          # Workflows for CI/CD (build → GHCR → ACA deploy)
```

## E. Success Criteria

* **Phase 0**: Repo, Aspire AppHost runs FE/API/Worker + Dapr locally; CI/CD builds containers, pushes to GHCR; ACA deploy; FE→Dapr→BE `/v1/health` works.
* **Phase 1**: End-to-end **streaming chat** via Next.js route handler → Dapr → API; sessions persisted in SQL; correlation IDs flow FE→BE (OTel visible).
* **Phase 2**: RAG with documents, citations visible; optional background ingestion via worker (can start synchronous, later Dapr pub/sub).
* **Phase 3–7**: Vision, insights, memory, agents, polish.
* Documentation and diagrams capture all design trade-offs.

## F. SSR & BFF Details

* **Reasons**: Lower FCP, SEO-ready pages, server-only secrets/config, streaming UI updates.
* **Implementation**: Next.js Route Handlers for API calls; edge runtime optional; use fetch with **Dapr app-id** for intra-mesh calls. Avoid exposing any `NEXT_PUBLIC_*` secrets.
* **Performance**: `minReplicas=1` for FE/API in ACA; compression and HTTP/2 enabled by default; stream tokens from backend to client.

## G. Aspire & Dapr Composition

* **Aspire** runs API + Worker with Dapr sidecars locally; single `AppHost` provides service discovery, health, and OpenTelemetry; local Cosmos/SQL emulation optional.
* Use **environment parity**: config via appsettings + env vars; Aspire `AppHost` mirrors ACA env variables.
* **Observability**: OTel spans emitted from FE (server routes) and BE; App Insights with sampling to stay in free tier.

## H. Event-Driven Approach (Selective)

* **Use cases**: Document ingestion, OCR, embeddings, batch sentiment.
* **Approach**: Start synchronous for simplicity; enable **Dapr pub/sub** later with a toggle. Keep chat path synchronous for latency and debuggability.

## I. Performance & Cost Controls

* Streaming everywhere; small chunk sizes in RAG; cache static assets aggressively.
* SQL connection pooling; reuse Blob/Cosmos clients.
* Telemetry sampling (App Insights) and log level caps to stay within free tier.

## J. Security & Networking Snapshot

* Frontend public with custom domain; backend internal to ACA environment.
* Dapr mTLS for sidecar-to-sidecar; ACA Envoy controls ingress.
* Secrets stored in GitHub Actions (PoC); plan for Managed Identity in later phase.
* Rate limiting/backoff on external AI calls; input validation on all tool endpoints.
