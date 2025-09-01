# Phase 0 Spec: Backend Foundation

**Component:** Backend (`/src/backend`)
**Source Plan:** [`docs/plans/Phased_Plan.md`](/docs/plans/Phased_Plan.md)
**Architectural Blueprint:** [`/docs/architecture/Backend-Architecture.md`](/docs/architecture/Backend-Architecture.md)

## 1. Goal

To scaffold a minimal .NET 8 Minimal API project that establishes a baseline for all future backend development. This includes setting up the core project structure with .NET Aspire orchestration integration, implementing a health check, and verifying the initial Semantic Kernel to Hugging Face model connectivity.

## 2. Feature Breakdown & Acceptance Criteria

### 2.1. Project Scaffolding

-   **Goal:** A clean, well-structured .NET project that follows modern best practices with .NET Aspire integration.
-   **Acceptance Criteria:**
    -   [ ] A new .NET 8 Minimal API project is created in `/src/backend`.
    -   [ ] The project structure strictly adheres to the **Vertical Slice Architecture** defined in the blueprint. The following directories **must** be created:
        -   `Domain/`: For core POCO entities.
        -   `Features/`: For individual feature slices.
        -   `Infrastructure/`: For external service implementations (AI, Persistence, Storage).
        -   `Shared/`: For cross-cutting concerns.
    -   [ ] A `Dockerfile` is present in `/src/backend`, capable of building a production-ready container image.
    -   [ ] The backend is configured to communicate with the worker service via Dapr service invocation when needed.
    -   [ ] The backend project is registered in the Aspire `AppHost` project for local orchestration.
    -   [ ] The backend is configured to work with Dapr sidecar when run through Aspire.
    -   [ ] The project is configured to support Native AOT compilation for improved startup performance and reduced memory footprint.

### 2.2. Health Check Endpoint

-   **Goal:** A simple endpoint to confirm the service is alive and responding.
-   **Acceptance Criteria:**
    -   [ ] A `GET` endpoint is implemented at `/v1/health`.
    -   [ ] The endpoint returns a `200 OK` status with a simple JSON body: `{"status": "Healthy"}`.
    -   [ ] This endpoint is implemented using the built-in ASP.NET Core Health Checks middleware.

### 2.3. Semantic Kernel Bootstrap & AI Ping

-   **Goal:** To verify the end-to-end connection from the backend service to the Hugging Face Inference Router through Semantic Kernel.
-   **Acceptance Criteria:**
    -   [ ] The `Microsoft.SemanticKernel` and `Microsoft.SemanticKernel.Connectors.OpenAI` NuGet packages are added to the project.
    -   [ ] The Semantic Kernel `Kernel` is registered in `Program.cs` for dependency injection.
    -   [ ] The Kernel is configured to use the Hugging Face Inference Router by pointing the OpenAI connector to the HF endpoint.
    -   [ ] The Hugging Face API token is loaded securely from an environment variable (e.g., `HUGGINGFACE_API_KEY`) and is **not** hardcoded.
    -   [ ] A `GET` endpoint is implemented at `/v1/ping-ai`.
    -   [ ] This endpoint uses the injected `Kernel` to invoke a simple, non-streaming prompt (e.g., "Ping").
    -   [ ] **On Success:** The endpoint returns a `200 OK` status with a JSON body containing the model's response, like `{"response": "Pong"}`.
    -   [ ] **On Failure:** The endpoint returns a `503 Service Unavailable` status with a descriptive error message if it cannot connect to the AI service.


## 3. Definition of Done (DoD)

-   [ ] The backend application can be successfully containerized using its `Dockerfile`.
-   [ ] When run locally via .NET Aspire, the `/v1/health` endpoint is reachable from the host machine.
-   [ ] The `/v1/ping-ai` endpoint successfully returns a response from the Hugging Face model, confirming the Semantic Kernel connection.
-   [ ] The application is successfully deployed to Azure Container Apps as part of the CI/CD pipeline.
-   [ ] The backend can successfully communicate with the worker service via Dapr service invocation when needed.
-   [ ] The backend successfully integrates with Aspire for local development orchestration.
-   [ ] The backend is configured to support Native AOT compilation for improved cold start performance in Azure Container Apps.