# Phased Implementation Plan

This is the active roadmap for the enterprise copilot app. It replaces the older Cosmos-first/Hugging Face-first planning direction with a **Search-first + Foundry/AI Services** architecture.

> Active platform direction: **Azure Container Apps + Dapr + Azure SQL + Blob Storage + Azure AI Search + AI Foundry/Azure AI Services**.

## Phase 0 — Platform Alignment

**Goal**

Stand up and align foundational infrastructure, runtime config, and deployment automation for the enterprise copilot stack.

**In Scope**

* Local orchestration with Aspire for frontend, backend, worker, SQL, Redis.
* Azure baseline provisioning for ACA, ACR, Log Analytics, App Insights, SQL, Blob, AI Search, and AI Services/Foundry dependencies.
* Unified runtime configuration for chat, embeddings, search, storage, and security.

**DoD**

* Local and cloud environments expose consistent configuration surfaces.
* CI/CD deploys the full baseline stack successfully.

## Phase 1 — Core Chat Assistant

**Goal**

Deliver reliable streaming chat with persistent conversation history and observability.

**In Scope**

* Chat endpoint and frontend chat UI.
* SQL persistence for sessions/messages.
* Tracing, correlation, and basic failure handling.

**DoD**

* Users can create/resume conversations with low-friction UX.
* Streaming responses and persistence work end to end.

## Phase 2 — Document Ingestion & Grounded RAG

**Goal**

Enable document ingestion and grounded answers with citations.

**In Scope**

* Blob-based upload flow.
* Worker extraction/chunking/embedding pipeline.
* Azure AI Search indexing and retrieval.
* Foundry/AI Services model usage for embeddings and response synthesis.

**DoD**

* Uploaded files become searchable and cited in answers.
* Retrieval and answer generation are functional in Azure.

## Phase 3 — Vision & OCR

**Goal**

Index scanned and image-based content into the same retrieval system.

**In Scope**

* OCR/scanned-document processing path.
* Image description/multimodal enrichment.
* Citation UX for image/page evidence.

**DoD**

* Users can retrieve facts from scanned/image assets with citations.

## Phase 4 — Insights & Analysis Workflows

**Goal**

Support enterprise analysis workloads beyond chat.

**In Scope**

* Batch summarization/classification/sentiment and related analysis jobs.
* Persisted job results and insights views in frontend.

**DoD**

* Users can run repeatable analysis workflows and review saved results.

## Phase 5 — Long-Term Memory

**Goal**

Build durable memory with controlled retrieval.

**In Scope**

* Conversation summarization job.
* Memory indexing/retrieval using AI Search plus SQL metadata.
* Prompt augmentation with scoped memory context.

**DoD**

* New conversations can leverage relevant prior context safely.

## Phase 6 — Agents & Tooling

**Goal**

Evolve from Q&A to enterprise task completion using safe tools.

**In Scope**

* Tool catalog (search lookup, document retrieval, SQL read-only query, exports).
* Tool-aware assistant/agent execution path.
* Frontend transparency for tool calls and outcomes.

**DoD**

* Multi-step enterprise tasks execute with clear auditability.

## Phase 7 — Hardening & Production Readiness

**Goal**

Harden security, quality, and operational maturity.

**In Scope**

* Entra authentication/authorization.
* Managed identity and least-privilege RBAC.
* Evaluation and monitoring for retrieval quality, grounding, latency, and cost.

**DoD**

* Security posture, reliability, and quality gates are documented and enforceable.

