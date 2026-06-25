# Setup Automation Implementation Notes

This document captures what changed to enable the one-click, cross-platform setup flow and where to find the canonical operational docs.

## Scope

The repository now supports an OS-aware setup experience for Windows, macOS, and Linux. The setup path is idempotent and intended for first-clone onboarding in VS Code.

## Implemented Components

1. Windows-native setup scripts
- scripts/setup-env.ps1
- scripts/setup.bat
- .vscode/install-extensions.ps1

2. Cross-platform wrapper
- scripts/setup.sh routes to the correct platform setup path

3. VS Code folder-open automation
- .vscode/tasks.json orchestrates dependency restore, tools checks, and local AI readiness in a deterministic order

4. Local AI bootstrap helpers
- .vscode/ensure-aspire-env.ps1
- .vscode/ensure-aspire-env.sh
- .vscode/ensure-docker.ps1
- .vscode/ensure-docker.sh
- .vscode/ensure-sql-connection-profile.ps1
- .vscode/ensure-sql-connection-profile.sh
- .vscode/ensure-local-llm.ps1
- .vscode/ensure-local-llm.sh
- .vscode/ensure-local-llm-ready.ps1
- .vscode/ensure-local-llm-ready.sh
- .vscode/install-dapr.ps1

## Behavior Summary

- On folder open, setup tasks prepare dependencies and local defaults without overwriting non-empty user values.
- Dapr and supporting tools are validated or installed at user scope where possible.
- Local llama.cpp setup and readiness checks run before full local launch.
- MSSQL extension profile `mssql-container` is auto-seeded into VS Code User Settings (`mssql.connections`).
- SQL profile host is set to `127.0.0.1`; port resolves from active Docker `sql-*` mapping when available, else `SQL_HOST_PORT`.

## Validation

- Run solution build:
  - dotnet build azure-aca-aspire-ai-starter.sln
- Run setup guard and architecture/integration tests:
  - dotnet test src/Backend.Tests/Backend.Tests.csproj

## Canonical Setup Docs

- docs/WINDOWS-SETUP.md
- docs/SETUP-CROSS-PLATFORM.md
- docs/LOCAL-AI-SETUP.md
- scripts/README.md

Use those documents for user-facing setup guidance. Keep this file as an implementation summary.