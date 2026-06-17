# 🛠️ Technical Blueprint: Cloud-Native ACA Aspire AI Starter

**A detailed guide for building the Azure ACA Aspire AI Starter Template on Azure, using Dapr, Azure AI Search, and Azure AI Foundry.**

---

### 📚 Key Facts & References

* **Semantic Kernel**: Microsoft’s open-source SDK for AI orchestration. [GitHub](https://github.com/microsoft/semantic-kernel) | [Docs](https://learn.microsoft.com/en-us/semantic-kernel/overview/)
* **Azure Container Apps**: Managed environment for running microservices and containerized applications. [Docs](https://learn.microsoft.com/en-us/azure/container-apps/)
* **Dapr**: APIs for building resilient, stateful, and event-driven distributed applications. [Docs](https://docs.dapr.io/)
* **Azure AI Search**: Managed retrieval and vector indexing for grounded responses. [Docs](https://learn.microsoft.com/en-us/azure/search/)
* **Azure AI Foundry / Azure OpenAI**: Managed model hosting and inference for chat and embeddings. [Docs](https://learn.microsoft.com/en-us/azure/ai-foundry/)

## 📋 A. Architecture & Technology Stack

* **Frontend**: React SPA + **Vite** + **Hono** Node host, managed with **npm**.
* **Backend**: .NET 10 Minimal APIs, **.NET Aspire** (AppHost & service discovery), Vertical Slice Architecture, Mediator (source-gen)
* **Infra**: Azure Container Apps (FE public, BE internal), Dapr sidecars for service invocation (and later pub/sub for ingestion).
* **Service Communication**: Dapr service invocation (FE → Dapr → BE); optional Dapr pub/sub for long-running ingestion/OCR in Phase 2+.
* **Data**:

  * Azure SQL Database (relational data)
  * Azure Blob Storage (file storage)
  * Azure AI Search (vector embeddings & retrieval)
* **AI Orchestration**:

  * Semantic Kernel
  * Azure AI Foundry / Azure OpenAI for chat + embeddings
* **IaC**: Bicep/Terraform scripts to provision ACA, SQL, Blob, Cosmos, network.
* **Deployment**: GitHub Actions → Azure Container Apps via `azd`, with service definitions in `azure.yaml`.
* **Observability**: ACA logs, optional App Insights + Log Analytics (free-tier friendly via sampling).

## B. Implementation Plan

See: [Enterprise_Ready_Execution_Plan.md](/docs/plans/Enterprise_Ready_Execution_Plan.md).

## C. Architecture Principles

* **Vertical Slice**: Features implemented end-to-end (API → Domain → Infra).
* **Mediator**: Source-generated mediator pattern for clean command/query separation.
* **Dapr-first**: FE invokes BE via Dapr sidecar app-id.
* **Cloud-native**: Stateless services, externalized state in SQL/Blob/Cosmos.
* **Cost-aware**: Free-tier defaults, enterprise add-ons only documented.
* **Extensible**: AI provider abstracted (SK service layer) to swap providers easily.
* **Thin frontend host**: The frontend stays as a Vite-built SPA served by a minimal Hono Node host, while backend integration remains explicit through Dapr and backend HTTP endpoints.
* **Aspire Orchestration**: Local composition of services with health/telemetry baked in.

## D. Repository Structure

```
/
 ├── src/
 │   ├── frontend/     # Vite React SPA served by a Hono Node host
 │   ├── backend/      # .NET Minimal API (VSA), SK orchestration
 │   ├── worker/       # .NET background worker (ingest/embeddings/OCR)
 │   └── aspire/       # AppHost & Aspire configuration for local orchestration
 ├── infra/            # Bicep/Terraform IaC for ACA, SQL, Blob, Cosmos, Dapr
 ├── docs/             # Background, Phased-Plan, Blueprint, Architecture, Decisions
 └── .github/          # Workflows for CI/CD (build → GHCR → ACA deploy)
```

## E. Success Criteria

* **Phase 0**: Repo, Aspire AppHost runs FE/API/Worker + Dapr locally; CI/CD builds containers, pushes to GHCR; ACA deploy; FE→Dapr→BE `/v1/health` works.
* **Phase 1**: End-to-end chat via the frontend Hono host and SPA → Dapr → API; sessions persisted in SQL; correlation IDs flow FE→BE (OTel visible).
* **Phase 2**: RAG with documents, citations visible; optional background ingestion via worker (can start synchronous, later Dapr pub/sub).
* **Phase 3–7**: Vision, insights, memory, agents, polish.
* Documentation and diagrams capture all design trade-offs.

## F. Frontend Host Details

* **Reasons**: Keep the frontend operationally simple, preserve a standard SPA build pipeline, and use a small Node host only for serving the app and environment-specific integration.
* **Implementation**: Build the frontend with Vite, manage dependencies with npm, and serve the app through the Hono-based Node host. Use backend or Dapr endpoints for server-side integration concerns rather than adopting a heavier server-rendered web framework.
* **Performance**: `minReplicas=1` for FE/API in ACA; compression and HTTP/2 enabled by default; keep client bundles lean and let the backend handle long-running AI and document workloads.

## G. Aspire & Dapr Composition

* **Aspire** runs API + Worker with Dapr sidecars locally; single `AppHost` provides service discovery, health, and OpenTelemetry; local Cosmos/SQL emulation optional.
* Use **environment parity**: config via appsettings + env vars; Aspire `AppHost` mirrors ACA env variables.
* **Observability**: OTel spans emitted from the frontend host and backend; App Insights with sampling to stay in free tier.

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
