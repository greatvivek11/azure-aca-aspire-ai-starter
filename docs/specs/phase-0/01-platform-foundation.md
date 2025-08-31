# Phase 0 Spec: Platform Foundation & Scaffolding

**Component:** Platform (Azure Infrastructure, Local Dev, CI/CD)  
**Source Plan:** [`docs/plans/Phased_Plan.md`](/docs/plans/Phased_Plan.md)

## 1. Goal

To establish the project's foundational scaffolding, including the repository structure, a functional local development environment using Docker Compose, and a CI/CD pipeline that deploys the initial containerized applications to a newly provisioned Azure environment.

## 2. Feature Breakdown & Acceptance Criteria

### 2.1. Repository & Local Development Setup

-   **Goal:** A developer can clone the repository and get a functional local environment running with a single command.
-   **Acceptance Criteria:**
    -   [ ] The repository is structured with top-level folders: `/frontend`, `/backend`, `/worker`, `/aspire`, `/infra`, `.github`.
    -   [ ] A `docker-compose.yml` file exists at the root.
    -   [ ] Running `docker compose up` successfully starts:
        -   The frontend React application container.
        -   The backend .NET application container.
        -   The worker .NET application container.
        -   A Dapr sidecar for the frontend container.
        -   A Dapr sidecar for the backend container.
        -   A Dapr sidecar for the worker container.
    -   [ ] The frontend application is accessible locally and can successfully call the backend's `/v1/health` endpoint via its Dapr sidecar.
    -   [ ] The frontend application is accessible locally and can successfully call the worker's `/v1/health` endpoint via its Dapr sidecar.

### 2.2. Infrastructure as Code (Bicep)

-   **Goal:** All Azure resources are defined as code for repeatable, version-controlled deployments.
-   **Acceptance Criteria:**
    -   [ ] Bicep files are located in the `/infra` directory.
    -   [ ] The Bicep template provisions the following resources, adhering to free/minimal SKUs where possible:
        -   [ ] **GitHub Container Registry (GHCR):** (Note: This is configured in GitHub, not Azure, but the pipeline will target it).
        -   [ ] **Azure Container Apps Environment:** 
            -   Configured for VNet integration.
            -   Associated with a new `Log Analytics Workspace`.
            -   Set to be `internal` to prevent public ingress to the environment itself.
        -   [ ] **Azure Container Apps:**
            -   `aca-aihub-frontend`: Configured for public ingress.
            -   `aca-aihub-backend`: Configured for **internal-only** ingress.
            -   `aca-aihub-worker`: Configured for **internal-only** ingress.
        -   [ ] **Dapr Configuration:**
            -   Dapr is enabled for `frontend`, `backend`, and `worker` container apps.
            -   The backend app has a Dapr app-id of `aihub-backend`.
            -   The frontend app has a Dapr app-id of `aihub-frontend`.
            -   The worker app has a Dapr app-id of `aihub-worker`.
        -   [ ] **Data Stores:**
            -   `Azure SQL Database`: Server and a single database.
            -   `Azure Blob Storage`: Storage account and a container.
            -   `Azure Cosmos DB`: Configured with the MongoDB vCore API and vector search enabled.
        -   [ ] **Identity & Security:**
            -   A single `User-Assigned Managed Identity` is created for the application.
            -   The backend container app is assigned this managed identity.
            -   The worker container app is assigned this managed identity.

### 2.3. CI/CD Pipeline (GitHub Actions)

-   **Goal:** Code pushed to the `main` branch is automatically built, tested, and deployed to Azure.
-   **Acceptance Criteria:**
    -   [ ] A workflow file exists at `.github/workflows/deploy.yml`.
    -   [ ] The workflow runs on pushes to the `main` branch.
    -   [ ] **Workflow Steps:**
        1.  [ ] **Authenticate to Azure:** The workflow securely logs into Azure using a pre-configured Service Principal stored in GitHub secrets.
        2.  [ ] **Deploy Infrastructure:** The `infra/main.bicep` file is deployed to the target resource group.
        3.  [ ] **Build & Test:** The workflow builds frontend, backend, and worker projects and runs any existing unit tests.
        4.  [ ] **Dockerize:** Docker images for frontend, backend, and worker are built.
        5.  [ ] **Push to GHCR:** The new images are tagged and pushed to GitHub Container Registry.
        6.  [ ] **Deploy to ACA:** The Azure Container Apps are updated with the new images, triggering a new revision.
    -   [ ] Secrets (e.g., `AZURE_CREDENTIALS`) are passed securely to the workflow from GitHub Actions secrets.

## 3. Definition of Done (DoD)

-   [ ] The CI/CD pipeline successfully completes on a push to `main`.
-   [ ] The frontend, backend, and worker applications are deployed and running in Azure Container Apps.
-   [ ] A developer can access the public URL of the deployed frontend application.
-   [ ] The deployed frontend successfully calls the backend's `/v1/health` endpoint through Dapr and displays a "Healthy" status.
-   [ ] The deployed frontend successfully calls the worker's `/v1/health` endpoint through Dapr and displays a "Healthy" status.
-   [ ] Basic logs from all applications are visible in the ACA Log Stream.