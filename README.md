# Azure ACA Aspire AI Starter

A cloud-native AI assistant template: chat with and run RAG over your documents, built on .NET 10, React, and Azure Container Apps. **One-click setup, debug, and deploy** — clone, launch the dev container, press F5 to debug, and GitHub Actions handles the rest.

## One-Click Experience

> ⚠️ **Prerequisite:** [Docker Desktop](https://www.docker.com/products/docker-desktop/) must be installed and running.

### Setup & Debug
1. **Clone** the repo.
2. **Open in VS Code (host workflow)** — folder-open tasks provision local defaults, dependencies, native llama.cpp readiness, and a local SQL Server connection profile for the MSSQL extension.
3. **Optional: Open in Dev Container** — use this when you want containerized toolchains; local AI still uses host-native llama.cpp endpoints via `host.docker.internal`.
4. **Press F5** — Aspire orchestrates the full local stack (frontend, backend, worker, SQL, Redis, Qdrant, Dapr).

### SQL Server Extension (VS Code)

- On folder open, the task `setup: ensure SQL Server connection profile` seeds a profile named `mssql-container` for extension `ms-mssql.mssql`.
- The profile is written to **VS Code User Settings** (`mssql.connections`), not workspace settings.
- The script targets `127.0.0.1` and attempts to resolve the active Docker SQL host port (`sql-*` container mapping to `1433`).
- If Aspire restarts and SQL is mapped to a new host port, run task `setup: ensure SQL Server connection profile` again before connecting.
- First connect may prompt for password (`sa` / `P@ssw0rd`) depending on local secure-store behavior.

### Deploy to Azure
1. **One-time bootstrap** — Run the CI bootstrap in an authenticated Azure shell to set up OIDC and a service principal.
	- macOS/Linux/WSL: `bash scripts/ci/bootstrap-ci-tenant-setup.sh --subscription-id "<your-subscription-id>" --github-owner "<owner>" --github-repo "<repo>"`
	- Windows PowerShell: follow the PowerShell bootstrap commands in [docs/GitHub-Secrets-Setup.md](./docs/GitHub-Secrets-Setup.md#1-create-service-principal-for-github-actions)
2. **Add GitHub secrets** — [5-min checklist](./docs/GitHub-Secrets-Setup.md#quick-bootstrap-checklist-5-10-min).
3. **Push to main** — GitHub Actions validates, builds, provisions infrastructure, and deploys automatically. (PRs validate only; merge to `main` to deploy.)

> Changes to docs or README skip the expensive deploy job — only code changes trigger redeployment.

## Features

- **Conversational AI + RAG** — chat grounded in your documents, with citations.
- **Document ingestion** — upload `.pdf`, `.docx`, and `.txt` files for indexing.
- **Local or Azure AI** — run fully local (llama.cpp-compatible server + Qdrant) or on Azure (Azure OpenAI + AI Search) via a single `AI_MODE` switch.
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
# 1. Clone and open in VS Code
git clone <repo-url> && cd azure-aca-aspire-ai-starter
# Folder-open tasks run automatically and prepare local defaults

# Optional: reopen in Dev Container for a containerized toolchain
# Local AI endpoints still target host-native llama.cpp via host.docker.internal

# 2. Optional manual env creation (folder-open tasks auto-create src/aspire/.env)
cp src/aspire/.env.example src/aspire/.env

# 3. Debug (F5 in VS Code, or from terminal:)
cd src/aspire && dotnet run
```

```powershell
# 1. Clone and open in VS Code
git clone <repo-url>
Set-Location azure-aca-aspire-ai-starter
# Folder-open tasks run automatically and prepare local defaults

# Optional: reopen in Dev Container for a containerized toolchain
# Local AI endpoints still target host-native llama.cpp via host.docker.internal

# 2. Optional manual env creation (folder-open tasks auto-create src/aspire/.env)
Copy-Item src/aspire/.env.example src/aspire/.env

# 3. Debug (F5 in VS Code, or from terminal:)
Set-Location src/aspire
dotnet run
```

Dev Container setup handles .NET 10 SDK, Node.js 20, Docker, Dapr, and Azure CLI. The `.env` file is gitignored. On workspace open, VS Code runs `setup: ensure local AI env defaults` to create `src/aspire/.env` when missing and populate required local AI defaults without overwriting non-empty values.

`src/aspire/.env.example` is the source of truth for default variable names and recommended values.

**Local mode** runs native host llama.cpp with Qdrant and Azurite — no Azure resources required. VS Code folder-open tasks bootstrap llama.cpp binaries, models, and local servers before F5. Auth is disabled by default for first-run. To enable Entra auth locally:
- macOS/Linux/WSL: `az login && bash scripts/setup-local-entra-auth.sh`
- Windows PowerShell: `az login; powershell.exe -ExecutionPolicy Bypass -File scripts/setup-local-entra-auth.ps1`

### Run modes

| Mode | Command | Communication | Best for |
| --- | --- | --- | --- |
| **vite-dev** | `ASPIRE_FRONTEND_MODE=vite-dev dotnet run` | Direct HTTP (HMR) | Fast frontend iteration |
| **container** (default) | `dotnet run` | Dapr service invocation | Production-like validation |

Details and troubleshooting: [docs/LOCAL-DEVELOPMENT-DAPR.md](./docs/LOCAL-DEVELOPMENT-DAPR.md).

## Testing

```sh
dotnet test src/Backend.Tests/Backend.Tests.csproj   # backend architecture + integration tests
npm run lint --prefix src/frontend                    # frontend lint
```

Workflow lint command by OS:
- macOS/Linux/WSL: `bash scripts/lint-workflows.sh`
- Windows PowerShell: run from Git Bash/WSL, or use the CI workflow run as the canonical lint gate

See [docs/Architecture-Tests.md](./docs/Architecture-Tests.md) for what the test suite enforces.

## Cloud Deployment

1. **Bootstrap (one-time):** Create a service principal and OIDC federated credential.
	- macOS/Linux/WSL: `bash scripts/ci/bootstrap-ci-tenant-setup.sh --subscription-id "<your-subscription-id>" --github-owner "<owner>" --github-repo "<repo>"`
	- Windows PowerShell: use the PowerShell bootstrap block in [docs/GitHub-Secrets-Setup.md](./docs/GitHub-Secrets-Setup.md#1-create-service-principal-for-github-actions)
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
| [Windows Setup](./docs/WINDOWS-SETUP.md) | Windows-first setup and troubleshooting |
| [Cross-Platform Setup](./docs/SETUP-CROSS-PLATFORM.md) | Setup flow across Windows, macOS, and Linux |
| [llama.cpp Setup](./docs/LLAMA_CPP_SETUP.md) | Native local AI setup details and validation |
| [Architecture Tests](./docs/Architecture-Tests.md) | Enforced backend boundaries |
| [CI/CD](./docs/CI-CD-GitHub-Actions.md) · [GitHub Secrets](./docs/GitHub-Secrets-Setup.md) | Deployment pipeline and configuration |
