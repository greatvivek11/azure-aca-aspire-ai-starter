# Phase 0 Spec: Worker Foundation

**Component:** Worker (`/src/worker`)
**Source Plan:** [`docs/plans/Phased_Plan.md`](/docs/plans/Phased_Plan.md)
**Architectural Blueprint:** [`/docs/architecture/Backend-Architecture.md`](/docs/architecture/Backend-Architecture.md)

## 1. Goal

To scaffold a minimal .NET background worker service that establishes a baseline for all future background processing tasks. This includes setting up the core project structure with .NET Aspire orchestration integration, implementing health checks, and verifying integration with the Aspire orchestration system and Dapr sidecar.

## 2. Feature Breakdown & Acceptance Criteria

### 2.1. Project Scaffolding

-   **Goal:** A clean, well-structured .NET background worker project that follows modern best practices with .NET Aspire integration.
-   **Acceptance Criteria:**
    -   [ ] A new .NET Worker project is created in the `/src/worker` directory.
    -   [ ] The project is configured to work with .NET Aspire for local development orchestration.
    -   [ ] A `Dockerfile` is present in the `/src/worker` directory, capable of building a production-ready container image for the application.
    -   [ ] The worker project references necessary packages for Dapr integration.
    -   [ ] The worker project is registered in the Aspire `AppHost` project for local orchestration.
    -   [ ] The project is configured to support Native AOT compilation for improved startup performance and reduced memory footprint.

### 2.2. Health Check Endpoint

-   **Goal:** A simple endpoint to confirm the worker service is alive and responding.
-   **Acceptance Criteria:**
    -   [ ] The worker implements a health check mechanism that can be queried via HTTP.
    -   [ ] A `GET` endpoint is implemented at `/v1/health`.
    -   [ ] The endpoint returns a `200 OK` status with a simple JSON body: `{"status": "Healthy", "service": "Worker"}`.
    -   [ ] This endpoint is implemented using the built-in ASP.NET Core Health Checks middleware.

### 2.3. Aspire Integration

-   **Goal:** To ensure the worker integrates properly with the .NET Aspire orchestration system.
-   **Acceptance Criteria:**
    -   [ ] The worker project is registered in the Aspire `AppHost` project.
    -   [ ] The worker can be started and stopped via Aspire locally.
    -   [ ] The worker is configured to work with Dapr sidecar when run through Aspire.
    -   [ ] Service discovery is configured so that other services can communicate with the worker if needed.

### 2.4. Dapr Integration

-   **Goal:** To establish the foundation for Dapr integration in the worker service.
-   **Acceptance Criteria:**
    -   [ ] The worker is configured to work with a Dapr sidecar locally and in Azure Container Apps.
    -   [ ] Dapr client is properly initialized and available for dependency injection.
    -   [ ] Basic Dapr service invocation is configured and tested.
    -   [ ] Dapr pub/sub components are configured (though not actively used in Phase 0).


## 3. Definition of Done (DoD)

-   [ ] The worker application can be successfully containerized using its `Dockerfile`.
-   [ ] When run locally via .NET Aspire, the `/v1/health` endpoint is reachable from the host machine.
-   [ ] When run via Aspire, the worker starts successfully and integrates with the Dapr sidecar.
-   [ ] The application is successfully deployed to Azure Container Apps as part of the CI/CD pipeline.
-   [ ] Basic logs from the worker are visible in the ACA Log Stream.
-   [ ] The worker is configured to support Native AOT compilation for improved cold start performance in Azure Container Apps.