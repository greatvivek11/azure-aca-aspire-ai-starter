# 🏛️ Backend Architecture

This document outlines the architectural decisions and patterns for the backend service of the AI-Powered Knowledge Hub. It separates the current implementation in the repository from the target direction for later phases.

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

The current backend is an operational foundation rather than the final feature-complete AI service.

* The application is a single `.NET 10` ASP.NET Core Minimal API project.
* `Program.cs` currently owns service registration, OpenTelemetry wiring, Dapr setup, SQL bootstrapping, and several HTTP endpoints.
* Two feature endpoints are already extracted into `Features/Health/Endpoint.cs` and `Features/AiPing/Endpoint.cs`.
* Customer CRUD endpoints are still mapped directly in `Program.cs` and use raw `SqlConnection` / `SqlCommand` access instead of EF Core.
* SQL schema initialization is performed from `Infrastructure/Sql/seed.sql` during startup.
* AI integration is abstracted behind `IAiService`, with `SemanticKernelService` using Semantic Kernel and Azure OpenAI-compatible configuration.
* Dapr is enabled in the service host, but the current backend does not yet expose pub/sub handlers or a broader event-driven workflow.

## 🔍 Architectural Refinements

While VSA provides the target organizational structure, the current backend uses a smaller set of refinements that can expand over time.

1. **Shared Infrastructure Folder**: The `Infrastructure` folder houses cross-cutting implementation details such as AI integration and SQL seed scripts.
2. **AI Service Abstraction**: `IAiService` and `SemanticKernelService` isolate prompt execution concerns from feature endpoints.
3. **Dapr Integration**: The service is Dapr-enabled for local and cloud orchestration, even though only HTTP invocation support is exercised today.
4. **SQL Access Simplicity**: The current implementation prefers direct ADO.NET usage for customer CRUD flows instead of introducing EF Core prematurely.
5. **Event-Driven Extension Path**: Aspire + Dapr still provide a path toward worker coordination, pub/sub, and more distributed workflows in later phases.

## 📦 Evolved Project Structure

```
/src/backend/
|
├── Domain/
│   └── Document.cs
│
├── Features/
│   ├── AiPing/
│   │   └── Endpoint.cs
│   └── Health/
│       └── Endpoint.cs
│
├── Infrastructure/
│   ├── Ai/
│   │   ├── IAiService.cs
│   │   ├── SemanticKernelService.cs
│   │   └── AzureOpenAiOptions.cs
│   └── Sql/
│       └── seed.sql
│
└── Program.cs
```

### Folder & Layer Responsibilities

1. **`Domain/`**: Contains core domain models. The current repository only has an initial `Document` model here.

2. **`Features/`**: Holds extracted endpoint slices. Today this includes health and AI ping endpoints; more domain slices can move here over time.

3. **`Infrastructure/`**:

   * **AI**: `SemanticKernelService` wraps Semantic Kernel and OpenAI-compatible configuration.
   * **SQL**: `seed.sql` initializes the current relational schema used by the customer CRUD sample.

4. **`Program.cs`**: The current composition root registers services, configures telemetry and Dapr, validates Azure OpenAI configuration, initializes SQL schema, and still hosts the inline customer endpoints.

## 🚀 Key Improvements with Aspire

* **Configuration-as-Code**: Aspire AppHost orchestrates the frontend, backend, worker, SQL, Redis, and Dapr dependencies.
* **Local Dev Parity**: Aspire runs all services locally with Dapr sidecars for testing.
* **Cloud-Native Ready**: Easy to extend into event-driven patterns without restructuring.
* **Minimal APIs First**: Fast to implement, still production-ready.
* **Telemetry Built-In**: OTel integration with Aspire surfaces traces across FE/BE services.

## Current Backend Responsibilities

The backend currently covers a narrower but concrete set of responsibilities:

* `/v1/health` readiness endpoint
* `/v1/ping-ai` connectivity test for the configured AI provider
* `/v1/customers` CRUD endpoints backed by SQL Server
* OpenTelemetry logging, tracing, and Azure Monitor export when configured
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
