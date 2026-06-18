# Azure ACA Aspire AI Starter

A cloud-native AI assistant template: chat with and run RAG over your documents, built on .NET 10, React, and Azure Container Apps. **One-click setup, debug, and deploy** — clone, launch the dev container, press F5 to debug, and GitHub Actions handles the rest.

## One-Click Experience

> ⚠️ **Prerequisite:** [Docker Desktop](https://www.docker.com/products/docker-desktop/) must be installed and running.

### Setup & Debug
1. **Clone** the repo.
2. **Open in Dev Container** — VS Code prompts to reopen in a container; `.devcontainer/` downloads .NET, Node.js, CLI tools, and all extensions.
3. **Press F5** — Aspire orchestrates the full local stack (frontend, backend, worker, SQL, Redis, Ollama, Qdrant, Dapr).

### Deploy to Azure
1. **One-time bootstrap** — Run `bash scripts/ci/bootstrap-ci-tenant-setup.sh` in an authenticated Azure shell to set up OIDC and a service principal.
2. **Add GitHub secrets** — [5-min checklist](./docs/GitHub-Secrets-Setup.md#quick-bootstrap-checklist-5-10-min).
3. **Push to main** — GitHub Actions validates, builds, provisions infrastructure, and deploys automatically. (PRs validate only; merge to `main` to deploy.)

> Changes to docs or README skip the expensive deploy job — only code changes trigger redeployment.

## Features

- **Conversational AI + RAG** — chat grounded in your documents, with citations.
- **Document ingestion** — upload `.pdf`, `.docx`, and `.txt` files for indexing.
- **Local or Azure AI** — run fully local (Ollama + Qdrant) or on Azure (Azure OpenAI + AI Search) via a single `AI_MODE` switch.
- **Secure by default** — Microsoft Entra ID auth, passwordless managed identity, private backend, rate limiting, and upload guardrails.

## Architecture

- **Frontend** — React SPA (Vite) served by a lightweight Hono Node host; MSAL/Entra auth.
- **Backend** — .NET 10 Minimal APIs, Vertical Slice Architecture, Dapr-enabled.
- **Worker** — .NET background worker for document ingestion.
- **Orchestration** — .NET Aspire composes services locally; Dapr handles service-to-service calls.
- **Data** — Azure SQL, Azure Blob Storage, and a vector store (Azure AI Search or Qdrant).
- **Platform** — Azure Container Apps, provisioned with Bicep and deployed via GitHub Actions.

See [docs/Architecture/Blueprint.md](./docs/Architecture/Blueprint.md) for the full design.

## Prerequisites

[.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) · [Node.js 20 + npm](https://nodejs.org/) · [Docker Desktop](https://www.docker.com/products/docker-desktop/) · [Dapr CLI](https://docs.dapr.io/getting-started/install-dapr-cli/) · [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli)

## Quick Start (Local)

```bash
# 1. Clone and open in Dev Container
git clone <repo-url> && cd azure-aca-aspire-ai-starter
# In VS Code: when prompted, reopen in container (downloads all deps)

# 2. Create your local env (defaults to AI_MODE=local; no Azure needed)
cp src/aspire/.env.example src/aspire/.env

# 3. Debug (F5 in VS Code, or from terminal:)
cd src/aspire && dotnet run
```

Dev Container setup handles .NET 10 SDK, Node.js 20, Docker, Dapr, and Azure CLI. The `.env` file is gitignored; for the full list of variables, see [src/aspire/.env.example](./src/aspire/.env.example).

**Local mode** runs Ollama, Qdrant, and Azurite as containers — no Azure resources required. Models are pulled on first run and cached in Docker volumes. Auth is disabled by default for first-run; to enable Entra auth locally, run `az login && bash scripts/setup-local-entra-auth.sh`.

### Run modes

| Mode | Command | Communication | Best for |
| --- | --- | --- | --- |
| **vite-dev** | `ASPIRE_FRONTEND_MODE=vite-dev dotnet run` | Direct HTTP (HMR) | Fast frontend iteration |
| **container** (default) | `dotnet run` | Dapr service invocation | Production-like validation |

Details and troubleshooting: [docs/LOCAL-DEVELOPMENT-DAPR.md](./docs/LOCAL-DEVELOPMENT-DAPR.md).

## Testing

```bash
dotnet test src/Backend.Tests/Backend.Tests.csproj   # backend architecture + integration tests
npm run lint --prefix src/frontend                    # frontend lint
bash scripts/lint-workflows.sh                        # GitHub Actions workflow lint
```

See [docs/Architecture-Tests.md](./docs/Architecture-Tests.md) for what the test suite enforces.

## Cloud Deployment

1. **Bootstrap (one-time):** Run `bash scripts/ci/bootstrap-ci-tenant-setup.sh` to create a service principal and OIDC federated credential.
2. **Configure secrets:** Follow the [5-minute checklist](./docs/GitHub-Secrets-Setup.md#quick-bootstrap-checklist-5-10-min) to add `AZURE_SUBSCRIPTION_ID`, `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, SQL credentials, etc.
3. **Push to main:** GitHub Actions runs validation, then provisions infrastructure (SQL, Blob, AI Search, AI Foundry) via Bicep, and deploys to Azure Container Apps using `azd`. Entra app registrations and Managed Identity RBAC are wired automatically.

**Doc-only changes skip deployment** — if you only modify files under `/docs` or `README.md`, the workflow validates code but skips the expensive provision/deploy steps.

Full reference: [CI/CD Pipeline](./docs/CI-CD-GitHub-Actions.md) and [GitHub Secrets Setup](./docs/GitHub-Secrets-Setup.md).

## Documentation

| Doc | Purpose |
| --- | --- |
| [Blueprint](./docs/Architecture/Blueprint.md) | Intended system design, stack, and AI modes |
| [Cloud Architecture](./docs/Architecture/Cloud-Architecture.md) | End-to-end deployment and security |
| [Backend Architecture](./docs/Architecture/Backend-Architecture.md) | Backend structure and patterns |
| [Frontend Architecture](./docs/Architecture/Frontend-Architecture.md) | Frontend stack and patterns |
| [Network Hardening Extension](./docs/Architecture/Network-Hardening-Extension.md) | Optional VNET/private-endpoint path |
| [Local Development with Dapr](./docs/LOCAL-DEVELOPMENT-DAPR.md) | Run modes and service communication |
| [Architecture Tests](./docs/Architecture-Tests.md) | Enforced backend boundaries |
| [CI/CD](./docs/CI-CD-GitHub-Actions.md) · [GitHub Secrets](./docs/GitHub-Secrets-Setup.md) | Deployment pipeline and configuration |
