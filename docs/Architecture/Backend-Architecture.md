# 🏛️ Backend Architecture

This document outlines the architectural decisions and patterns for the backend service of the Azure ACA Aspire AI Starter Template. It separates the current implementation in the repository from the target direction for later phases.

## 🎯 Core Principles

1. **Feature-Oriented Growth**: Organize reusable endpoint logic by feature where it improves clarity, while allowing the initial implementation to stay compact.
2. **Pragmatism over Dogma**: The architecture should serve the project's needs without adding unnecessary complexity. Avoid patterns that add more ceremony than value for the current phase.
3. **Single Project Simplicity with Aspire Orchestration**: Maintain all backend code within a single ASP.NET Core project while using **.NET Aspire** to compose, configure, and run distributed services across ACA.
4. **Progressive Extraction**: Keep the initial backend operational even if some logic still resides in `Program.cs`, then extract slices and infrastructure services as the domain surface grows.

## 🏗️ Architectural Pattern: Vertical Slice Architecture (VSA)

The repository is moving toward **Vertical Slice Architecture (VSA)** as its primary organizational pattern.

### What is VSA?

VSA is a way of structuring code where all the components required for a single feature or use case are located together. This includes the API endpoint, business logic, data access, and any request/response models.

### VSA vs. Clean/Onion Architecture

VSA is not a replacement for the principles of Clean Architecture (like separation of concerns), but rather a different approach to applying them.

* **Clean/Onion Architecture (Horizontal Slicing)**: Organizes code into technical layers (e.g., `Project.Domain`, `Project.Application`, `Project.Infrastructure`). This forces you to work across multiple projects for a single feature.
* **Vertical Slice Architecture**: Organizes code by feature. This promotes high cohesion within a feature and low coupling between features.

For this project, the backend remains a **single ASP.NET Core project** managed and deployed with **Aspire AppHost** for configuration and local orchestration. The current codebase already extracts some endpoint slices and infrastructure services, while other logic remains inline in the composition root.

### Why VSA for This Project?

* **High Cohesion**: All code related to a feature lives in one place, making it easier to find, understand, and modify.
* **Low Coupling**: Slices are self-contained and have minimal dependencies on each other. Changing one feature is unlikely to break another.
* **Improved Developer Experience**: Reduces context-switching and the need to navigate a complex project structure.
* **Aspire Alignment**: Keeps service orchestration declarative while maintaining backend code simple and feature-focused.

## Current Implementation Snapshot

The current backend is an operational foundation with chat, RAG, and ingestion slices in place.

* The application is a single `.NET 10` ASP.NET Core Minimal API project.
* `Program.cs` is a thin composition root: service registration, OpenTelemetry wiring, Dapr setup, auth, and startup tasks are delegated to `Infrastructure/` modules.
* Feature endpoints are extracted into slices under `Features/`: `Health`, `AiPing`, `Chat`, `Customers`, and `DocumentIngestion`.
* Data access uses raw `SqlConnection` / `SqlCommand` (no EF Core); schema is initialized from `Infrastructure/Sql/seed.sql` during startup.
* AI integration is abstracted behind `IAiService`, implemented by `OllamaChatService` (local mode) and `FoundryChatService` (azure mode), selected via the `AI_MODE` environment variable.
* Dapr is enabled for service invocation; the backend triggers the worker for ingestion and registers a subscribe handler for status updates.

## 🔍 Architectural Refinements

While VSA provides the target organizational structure, the current backend uses a smaller set of refinements that can expand over time.

1. **Shared Infrastructure Folder**: The `Infrastructure` folder houses cross-cutting concerns: AI integration, Entra auth, logging, startup tasks, and SQL seed scripts.
2. **AI Service Abstraction**: `IAiService` with `OllamaChatService` and `FoundryChatService` isolates the chat/embedding provider from feature endpoints.
3. **Dapr Integration**: The service is Dapr-enabled; the backend invokes the worker via Dapr service invocation for ingestion.
4. **SQL Access Simplicity**: The implementation uses direct ADO.NET via a DI-backed store abstraction rather than introducing EF Core.
5. **Event-Driven Extension Path**: Aspire + Dapr provide a path toward broader pub/sub workflows in later phases.

## 📦 Evolved Project Structure

```
/src/backend/
├── Domain/
│   └── Document.cs
├── Features/
│   ├── AiPing/Endpoint.cs
│   ├── Chat/Endpoint.cs
│   ├── Customers/Endpoint.cs
│   ├── DocumentIngestion/
│   │   ├── Endpoint.cs
│   │   └── DocumentIngestionStore.cs
│   └── Health/Endpoint.cs
├── Infrastructure/
│   ├── Ai/
│   │   ├── IAiService.cs
│   │   ├── OllamaChatService.cs
│   │   ├── FoundryChatService.cs
│   │   ├── AzureOpenAiOptions.cs
│   │   └── AzureOpenAiRuntimeSettings.cs
│   ├── Auth/
│   │   ├── EntraAuthSetup.cs
│   │   └── EntraAuthOptions.cs
│   ├── Logging/LogSanitizer.cs
│   ├── Sql/seed.sql
│   └── Startup/
│       ├── BackendRuntimeOptions.cs
│       ├── BackendStartupTasks.cs
│       ├── BackendRequestLoggingExtensions.cs
│       └── UploadProtectionExtensions.cs
└── Program.cs
```

### Folder & Layer Responsibilities

1. **`Domain/`**: Core domain models (currently a `Document` model).

2. **`Features/`**: Endpoint slices — `Health`, `AiPing`, `Chat` (general + RAG), `Customers` (CRUD), and `DocumentIngestion` (upload/ingest/status).

3. **`Infrastructure/`**:

   * **Ai**: `IAiService` with `OllamaChatService` (local) and `FoundryChatService` (azure).
   * **Auth**: Entra ID JWT bearer setup and options.
   * **Logging**: log sanitization helpers.
   * **Sql**: `seed.sql` initializes the relational schema (`Customers`, `DocumentIngestionJobs`).
   * **Startup**: runtime option binding, startup tasks, request logging, and upload protection.

4. **`Program.cs`**: A thin composition root that registers services, configures telemetry, Dapr, and auth, and delegates startup tasks to the `Infrastructure/Startup` modules.

## 🚀 Key Improvements with Aspire

* **Configuration-as-Code**: Aspire AppHost orchestrates the frontend, backend, worker, SQL, Redis, and Dapr dependencies.
* **Local Dev Parity**: Aspire runs all services locally with Dapr sidecars for testing.
* **Cloud-Native Ready**: Easy to extend into event-driven patterns without restructuring.
* **Minimal APIs First**: Fast to implement, still production-ready.
* **Telemetry Built-In**: OTel integration with Aspire surfaces traces across FE/BE services.

## Current Backend Responsibilities

The backend covers a concrete set of responsibilities:

* `/v1/health` readiness endpoint (anonymous)
* `/v1/ping-ai` connectivity test for the configured AI provider
* `/v1/chat` general chat and document-grounded RAG (with citations)
* `/v1/customers` CRUD endpoints backed by SQL
* `/v1/uploads`, `/v1/uploads/signed-url`, `/v1/ingest`, `/v1/uploads/{id}/status` for document ingestion
* Entra ID JWT authorization on feature endpoints (scope `access_as_user`)
* OpenTelemetry logging/tracing with Azure Monitor export when configured
* SQL schema seeding on startup

## Evolution Path

Later phases can evolve the backend in these directions without changing the overall deployment model:

* Move inline CRUD and domain workflows out of `Program.cs` into dedicated feature slices.
* Introduce richer domain models and request/response types beyond the current records.
* Add worker-coordinated ingestion, retrieval, and long-running AI workflows.
* Introduce more formal persistence abstractions only when the domain surface justifies them.

## ✅ Success Criteria

* The backend remains easy to run locally through Aspire and easy to deploy to ACA.
* Feature extraction continues without forcing unnecessary project or layer proliferation.
* AI, SQL, telemetry, and Dapr integration points stay explicit and testable.
* The codebase remains ready for extension into richer vertical slices and async workflows in later phases.
