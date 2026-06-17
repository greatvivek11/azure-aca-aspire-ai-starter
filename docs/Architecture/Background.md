# Project: Azure ACA Aspire AI Starter Template (Cloud-Native Edition)

An enterprise-grade, AI-powered assistant template built on a modern, scalable, and secure cloud architecture using Azure Container Apps, Dapr, Azure SQL, Azure Blob Storage, Azure AI Search, and Azure AI Foundry / Azure OpenAI.

---

# Background

This project is designed as a **minimal reusable starter template** for modern enterprise AI application development with .NET, React, and Azure. The goal is to provide production-aligned cloud wiring with fast setup and low operational complexity.

## Vision

Build an **Azure ACA Aspire AI Starter Template** combining conversational AI, document retrieval (RAG), sentiment analysis, vision/OCR, and insights dashboards. It will be production-like but scoped to free-tier friendly PoC deployments.

## Why This Project Matters

* **Enterprise relevance**: Azure-first deployment (ACA, SQL, Blob, AI Search).
* **Modern stack**: .NET 10 Minimal APIs **+ .NET Aspire**, React + **Vite** + **Hono**, Dapr.
* **AI integration**: Azure AI Foundry / Azure OpenAI for chat and embeddings.
* **Architecture**: Vertical Slice Architecture, Mediator (source-gen), repository pattern, IaC (Bicep/Terraform).
* **Showcase**: Demonstrates frontend, backend, AI/ML integration, cloud-native deployment, and CI/CD.

## Use Case

This application enables employees to:

* Chat with an AI assistant that searches and reasons over company documents.
* Upload documents/images (PDF, DOCX, TXT, images) to Azure Blob Storage.
* Ask grounded questions and get answers with citations from ingested documents.
* Analyze sentiment of customer feedback and internal notes.
* Retain long-term memory of conversations and user preferences.
* Perform image analysis and OCR for scanned/visual data.
* Use "Ask Me Anything" for general purpose queries.

## Key Features & Cloud Architecture

### Azure-first, Dapr-enabled

* **Hosting**: Azure Container Apps (ACA) hosts both frontend and backend as separate container apps.
* **Frontend Host**: A **Vite-built React SPA** served by a lightweight **Hono** Node application. The host can expose same-origin proxy routes and operational endpoints without adopting a full SSR framework.
* **Backend**: .NET 10 Minimal APIs (Vertical Slice Architecture) with **.NET Aspire** for orchestration, service discovery, and OpenTelemetry.
* **Service Invocation**: Dapr sidecars provide secure FE → BE invocation (FE → Dapr → BE), retries, mTLS, and service discovery.
* **Data**:

  * Azure SQL Database → relational data (users, chat sessions, messages, audit).
  * Azure Blob Storage → binary objects (documents, images).
  * Azure AI Search → embeddings index, RAG chunks, and retrieval.
* **Networking**:

  * Custom domain for FE (public).
  * Private internal networking for BE within ACA; limit access to Dapr service invocation only. Envoy-based ingress managed by ACA.
* **AI Orchestration**:

  * Semantic Kernel (SK) as coordinator.
  * Azure AI Foundry / Azure OpenAI for chat and embeddings.
* **Registry & CI/CD**:

  * Images published to **GHCR** (GitHub Container Registry); ACA pulls via registry secret in consumption plan.
  * GitHub Actions build & deploy with ACA revisions.

### Architectural Updates (Aug 2025)

* **Adopt .NET Aspire** to compose the API and a background **worker** (ingestion/OCR/embeddings) locally and to standardize telemetry and health checks.
* **Use a lightweight Hono host** to serve the SPA, expose health and proxy routes, and keep sensitive integration logic out of the browser where needed.
* **Event-Driven (selective)**: Keep chat synchronous; consider **Dapr pub/sub** only for ingestion and batch processing from Phase 2 onward to avoid over-engineering.
* **Cost-Aware Defaults**: ACA consumption plan, `minReplicas=1` for FE/API to reduce cold starts; worker allowed to scale to zero. LAW/App Insights with sampling to stay within free tier.

## Enterprise Relevance

* Cloud-native microservices with ACA + Dapr.
* Secure data paths (private networking, least privilege, managed identities later).
* Real-world AI patterns: RAG, multimodal, memory, structured outputs, agents.
* Modern .NET & React best practices (Vertical Slice, Minimal APIs, **.NET Aspire**, Vite-based SPA delivery, lightweight Node hosting).

## Roadmap

See: [Enterprise_Ready_Execution_Plan.md](/docs/plans/Enterprise_Ready_Execution_Plan.md) for the current execution plan and milestones.

## Constraints

* No personal Azure spend: use free tiers, consumption plans, and minimal SKUs.
* Secrets via GitHub Actions only (no Key Vault).
* No Private Endpoints or Defender for Cloud (documented but not implemented).
* Focus on **learning + showcasing**, not enterprise hardening.

## Outcomes

* Functional, cloud-hosted application accessible with custom domain (frontend SPA served by Hono).
* Backend internal to ACA, invoked via Dapr; Aspire orchestrates services and observability.
* Documentation of design decisions, trade-offs, and enterprise-ready recommendations.
* Resume-ready GitHub repo with clean code, docs, and architecture diagrams.
