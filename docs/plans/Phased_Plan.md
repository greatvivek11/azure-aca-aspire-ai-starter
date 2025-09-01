# Phased Implementation Plan

This plan is intentionally **high-level but actionable**. It defines scope, milestones, acceptance criteria (DoD), and risks for each phase. It is the only place where phases are maintained. Other docs should link here.

> Constraints: PoC/resume project, cost-sensitive (avoid paid services like Private Endpoints, Key Vault, Defender for Cloud). Azure Container Apps (ACA) + Dapr, Azure SQL, Blob Storage, Cosmos DB (vector), Semantic Kernel (SK) + Hugging Face Inference Router via OpenAI connector. Frontend: React 19 + **Next.js SSR/BFF**, Tailwind v4. Backend: .NET 8 Minimal APIs **with .NET Aspire**, Vertical Slice Architecture, **Mediator (source-gen) instead of MediatR**.

## Phase 0 — Cloud Foundation & Scaffolding

**Goals**

* Establish repo, CI/CD, infra, Aspire composition, Dapr service invocation, and SK→HF connectivity.

**Scope (In)**

* Monorepo structure, Dockerfiles, base GitHub Actions.
* Provision: GHCR, ACA env, ACA apps (FE public with SSR, BE internal), Azure SQL, Blob, Cosmos (vector) — all minimal SKUs/free tiers.
* Dapr enabled for FE, BE, Worker; FE SSR route handler invokes BE via Dapr app-id.
* Aspire AppHost runs FE, API, Worker with Dapr locally; provides service discovery + OpenTelemetry.
* Secrets via GitHub Actions; Managed Identity planned later.
* Health endpoints and smoke tests.

**Scope (Out)**

* Private Endpoints, Key Vault, Defender for Cloud (documented as optional).

**Key Tasks**

* Repo scaffold: `/frontend`, `/backend`, `/worker`, `/aspire`, `/infra`, `.github/workflows`.
* Dockerize FE/BE/Worker; local dev with Aspire AppHost + Dapr sidecars.
* Worker implements basic health checks and integrates with Dapr.
* Bicep/Terraform: create ACA env + apps, GHCR, SQL (basic), Blob, Cosmos (Mongo vCore + vector).
* Configure FE public ingress + custom domain (optional), BE internal-only.
* Add `/v1/health` in API; FE SSR route handler invokes it via Dapr.
* Kernel bootstrap: SK + HF Router via OpenAI connector.
* FE “Status” page shows API health.

**DoD**

* CI builds containers, pushes to GHCR on main.
* Aspire runs FE/API/Worker with Dapr locally.
* ACA deploys FE+BE+Worker; FE→Dapr→BE `/v1/health` returns 200; FE→Dapr→Worker `/v1/health` returns 200.
* Basic OTel traces/logs visible (console/ACA logs).

**Risks & Mitigations**

* Aspire+Dapr config drift → encode in `aspire/AppHost` and IaC.
* ACA SSR cold starts → set `minReplicas=1` for FE.

## Phase 1 — Core Chat Assistant

**Goals**

* End-to-end chat with streaming, persisted sessions/messages, and telemetry.

**Scope (In)**

* `/v1/chat` endpoint using SK with HF Router.
* Streaming responses to FE (Next.js Route Handler with fetch/stream).
* EF Core + Azure SQL for sessions/messages.
* **Mediator (source-gen)** for lightweight CQRS per slice (or keep pure VSA).

**Key Tasks**

* Domain: `User`, `Conversation`, `Message` entities; migrations.
* Feature slice: `Chat/` with `Endpoint`, `Command/Query`, `Handlers`.
* FE chat UI: SSR page + client hydration; session switcher, message list, regenerate, error states.
* Observability: correlation id from FE SSR → API, OTel spans.

**DoD**

* Multiple concurrent sessions per user.
* Messages persisted; reload restores history.
* Streaming works smoothly; FE renders tokens progressively.

**Risks**

* HF Router rate limits → add backoff + retry in SK service.

## Phase 2 — Document Ingestion & RAG

**Goals**

* Upload → extract → chunk → embed → vector store; Q\&A over docs with citations.

**Scope (In)**

* Signed upload to Blob; metadata in SQL.
* Worker handles extraction & embedding; orchestrated by Aspire.
* Extraction: text from PDF/DOCX/TXT (OSS libs).
* Chunking (token-aware) + embeddings via HF models.
* Cosmos vector upsert; retrieval with citation metadata.
* FE: upload page with progress; toggle: “Ask about docs”.

**Key Tasks**

* API: `/v1/uploads/signed-url`, `/v1/ingest`.
* Worker service: extraction & embedding flow.
* Data model: `Document`, `Chunk` (id, docId, span, vector, sourceUri).
* FE: doc uploads UI, citations in answers.

**DoD**

* Upload doc; ask a question; see grounded answer with citations.
* Cosmos vector index populated; acceptable latency.

**Risks**

* Large PDFs → cap file size, offload to ACA Job.

## Phase 3 — Vision & OCR

**Goals**

* Image analysis and OCR for image-only PDFs; index extracted text for RAG.

**Scope (In)**

* Image ingestion to Blob; OCR (Tesseract/PaddleOCR container) or HF vision model.
* Extracted text piped into Phase-2 ingestion.
* FE: image preview + extracted text snippet.

**DoD**

* Upload image; ask about its content; see citations referencing image/page.

**Risks**

* OCR accuracy → allow re-run or correction.

## Phase 4 — Sentiment & Insights

**Goals**

* Batch sentiment/classification and a small insights dashboard.

**Scope (In)**

* `/v1/sentiment/batch` endpoint; HF classifier.
* Store scores + aggregates in SQL.
* FE dashboard with charts, filters.

**DoD**

* Upload CSV/JSON; run analysis; view trends and distributions.

**Risks**

* Model noise → show confidence.

## Phase 5 — Long-Term Memory

**Goals**

* Summarize and store conversation memories; personalize retrieval context.

**Scope (In)**

* Worker job: scheduled summary per conversation; memory vectors in Cosmos.
* API loads relevant snippets into SK system prompt.

**DoD**

* Returning session leverages prior memory context.

**Risks**

* Prompt bloat → cap/summarize aggressively.

## Phase 6 — Agents & Tooling

**Goals**

* Tool-augmented assistant with a few safe, auditable tools.

**Scope (In)**

* SK tools: `blob.lookup`, `sql.readonly.query`, `csv.export`.
* Planner/routing logic.
* FE timeline shows tool calls and results.

**DoD**

* Assistant invokes a tool, displays reasoning and result.

**Risks**

* Tool abuse → strict input validation.

## Phase 7 — Hardening & Optimization

**Goals**

* Enterprise polish while staying cost-aware.

**Scope (In)**

* Entra ID authN/Z (optional PoC).
* Managed Identity to Blob/Cosmos/SQL.
* Response caching, quotas, prompt/temperature tuning.

**DoD**

* Security and cost model documented.

**Risks**

* Over-engineering → keep optional by default.

## Cross-Cutting: Testing & Quality

* **Unit**: handlers, services, chunking, embeddings adapter.
* **Integration**: FE SSR route → Dapr → BE; SQL/Blob/Cosmos adapters.
* **Smoke**: health checks; simple chat/upload flows post-deploy.
* **AI Eval**: prompt regression, RAG hit-rate sampling.

## Project Ops

* **GitHub Labels**: `phase:p0`..`phase:p7`, `area:fe`, `area:be`, `infra`, `ai`, `docs`.
* **Milestones**: P0..P7 with DoD copied.
* **Docs**: Each phase updates `DECISIONS.md`.
* **Cost Guardrails**: monitor ingestion volume; enable sampling.
