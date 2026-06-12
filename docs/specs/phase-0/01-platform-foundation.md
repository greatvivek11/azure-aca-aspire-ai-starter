# Phase 0 Spec: Platform Foundation & Alignment

**Component:** Platform (Azure Infrastructure, Local Dev, CI/CD)  
**Source Plan:** [`docs/plans/Phased_Plan.md`](/docs/plans/Phased_Plan.md)  
**Architectural Blueprint:** [`/docs/Architecture/Cloud-Architecture.md`](/docs/Architecture/Cloud-Architecture.md)

## 1. Goal

Establish a deployable foundation for the enterprise copilot architecture using Aspire locally and Azure-managed dependencies in cloud environments.

## 2. Feature Breakdown & Acceptance Criteria

### 2.1. Repository & Local Development

- **Goal:** Developers can run a consistent local baseline.
- **Acceptance Criteria:**
    -   [ ] Source layout remains organized under `/src/backend`, `/src/frontend`, `/src/worker`, and `/src/aspire`.
    -   [ ] Aspire starts frontend, backend, worker, SQL, and Redis with required Dapr sidecars.
    -   [ ] Local configuration supports chat model, embeddings model, search, and storage settings.
    -   [ ] Frontend can call backend and worker health endpoints through the local service path.

### 2.2. Infrastructure as Code (Bicep)

- **Goal:** Azure dependencies are provisioned as code and match the target enterprise copilot platform.
- **Acceptance Criteria:**
    -   [ ] Bicep in `/infra` provisions baseline resources for:
        -   [ ] Container Apps environment and app services (frontend/backend/worker)
        -   [ ] ACR, Log Analytics, and Application Insights
        -   [ ] Azure SQL database
        -   [ ] Azure Storage (Blob containers for ingestion/content)
        -   [ ] Azure AI Search (knowledge and memory indexes)
        -   [ ] AI Foundry/Azure AI Services-aligned model endpoint dependencies
    -   [ ] Backend and worker use a user-assigned managed identity.
    -   [ ] Required RBAC access for SQL, Blob, AI Search, and AI model endpoints is defined or documented.
    -   [ ] Deployment outputs expose runtime values consumed by `azd` and CI/CD.

### 2.3. CI/CD Pipeline

- **Goal:** Mainline changes are validated and deployed with environment parity.
- **Acceptance Criteria:**
    -   [ ] `.github/workflows/deploy.yml` builds, tests, provisions, and deploys the stack.
    -   [ ] Workflow configuration includes required settings for SQL, Blob, AI Search, and model deployments.
    -   [ ] Validation scripts fail fast when mandatory cloud dependencies are missing or misconfigured.

## 3. Definition of Done (DoD)

-   [ ] Local Aspire and Azure deployments share aligned config contracts.
-   [ ] The enterprise copilot baseline stack deploys successfully.
-   [ ] Documentation reflects Search-first/Foundry-first direction (not Cosmos-first).
