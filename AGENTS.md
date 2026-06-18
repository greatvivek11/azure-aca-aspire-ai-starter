# Project Guidelines

## Scope
This repository is a cloud-native AI application with four main code areas under `src/`:
- `src/aspire`: local orchestration and service composition
- `src/backend`: ASP.NET Core Minimal API backend
- `src/worker`: background worker service
- `src/frontend`: Vite + React frontend

## Source Of Truth
- Prefer the current repository layout and project manifests over narrative docs when they conflict.
- Treat `azure-aca-aspire-ai-starter.sln`, `azure.yaml`, `src/*/*.csproj`, `src/frontend/package.json`, and `src/aspire/AppHost.cs` as implementation truth.
- Some docs describe planned or earlier states of the system. If a doc conflicts with live code, follow the live code and call out the mismatch.

## Architecture
- The backend follows Vertical Slice Architecture with code organized under `Features`, `Infrastructure`, and `Domain`.
- Keep backend features independent. Do not introduce cross-feature coupling inside `src/backend/Features`.
- Local development is orchestrated from `src/aspire/AppHost.cs`, which wires backend, worker, frontend, SQL, Redis, and Dapr sidecars together.
- Azure deployment configuration is defined in `azure.yaml` and `infra/`.

## Build And Test
- Build the .NET solution from the repo root with `dotnet build azure-aca-aspire-ai-starter.sln`.
- Run backend architecture tests with `dotnet test src/Backend.Tests/Backend.Tests.csproj`.
- Frontend commands should run from `src/frontend` and use npm because `package-lock.json` is present.
- For local full-stack runs, prefer the Aspire host in `src/aspire`.

## Editing Conventions
- Keep changes minimal and scoped to the user request.
- Do not rename or relocate top-level projects unless the task requires it.
- Preserve the existing backend structure and naming conventions instead of introducing new layers.
- When changing architecture-sensitive code, check `docs/Architecture-Tests.md` and keep those constraints intact.
- When working on Azure deployment or environment setup, consult the docs under `docs/` before changing `infra/`, workflow, or bootstrap files.

## Useful References
- `README.md` for setup and deployment overview
- `docs/Architecture/Blueprint.md` for the intended system design
- `docs/Architecture-Tests.md` for enforced backend boundaries
- `docs/CI-CD-GitHub-Actions.md` and `docs/GitHub-Secrets-Setup.md` for delivery and environment setup