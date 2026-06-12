# Phase 0 Spec: Backend Foundation

**Component:** Backend (`/src/backend`)  
**Source Plan:** [`docs/plans/Phased_Plan.md`](/docs/plans/Phased_Plan.md)  
**Architectural Blueprint:** [`/docs/Architecture/Backend-Architecture.md`](/docs/Architecture/Backend-Architecture.md)

## 1. Goal

Create the backend baseline for enterprise copilot capabilities: reliable health, strong configuration validation, and Semantic Kernel integration against AI Foundry/Azure OpenAI-compatible endpoints.

## 2. Feature Breakdown & Acceptance Criteria

### 2.1. Project Scaffolding

- **Goal:** Maintain a clean vertical-slice baseline ready for feature growth.
- **Acceptance Criteria:**
    -   [ ] Backend remains structured with `Domain`, `Features`, and `Infrastructure` boundaries.
    -   [ ] Container build and Aspire registration are functional.
    -   [ ] Dapr integration is enabled for local and cloud runs.

### 2.2. Health & Diagnostics

- **Goal:** Verify service health and observability.
- **Acceptance Criteria:**
    -   [ ] `GET /v1/health` returns healthy status.
    -   [ ] Structured logs/traces are emitted and flow to OpenTelemetry/Azure Monitor when configured.
    -   [ ] Startup fails fast with clear messages when required AI configuration is missing.

### 2.3. Semantic Kernel Bootstrap & AI Ping

- **Goal:** Confirm backend connectivity to the configured chat model endpoint.
- **Acceptance Criteria:**
    -   [ ] Semantic Kernel and OpenAI-compatible connector are registered.
    -   [ ] Configuration targets AI Foundry/Azure OpenAI-compatible endpoint and deployment IDs.
    -   [ ] `GET /v1/ping-ai` validates model invocation.
    -   [ ] Configuration shape supports future split between chat and embeddings deployments.

## 3. Definition of Done (DoD)

-   [ ] Backend builds and runs in Aspire and Azure.
-   [ ] Health and AI ping endpoints succeed with configured runtime settings.
-   [ ] Foundation is aligned to Search-first + Foundry/AI Services roadmap.
