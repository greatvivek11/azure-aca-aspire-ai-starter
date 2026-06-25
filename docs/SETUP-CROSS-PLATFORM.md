# One-Click Setup & Deploy — Cross-Platform Guide

This guide covers the updated one-click setup that now supports **Windows, macOS, and Linux** with no admin rights required.

## Prerequisites

**All platforms:**
- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Node.js 20 + npm](https://nodejs.org/)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (running)

**Optional (recommended):**
- [Git Bash](https://git-scm.com/) or [WSL2](https://learn.microsoft.com/en-us/windows/wsl/install) on Windows (only for Bash-only helper scripts)

## Setup (Local Development)

### Windows (PowerShell)

**Option 1: Double-click setup (easiest)**
```
Right-click scripts/setup.bat → Run as administrator (NOT required, but may help if no admin)
```

**Option 2: PowerShell from VS Code terminal**
```powershell
powershell.exe -ExecutionPolicy Bypass -File scripts/setup-env.ps1
```

**Option 3: Auto-run on folder open**
When you open the workspace in VS Code, the following tasks run automatically:
- `setup: install all extensions` — installs VS Code extensions
- `setup: ensure SQL Server connection profile` — seeds `mssql-container` in VS Code User Settings (`mssql.connections`)
- `npm: install frontend` — npm ci
- `dotnet: restore backend/worker` — dotnet restore

### macOS

```bash
# Option 1: Wrapper script (auto-detects OS)
bash scripts/setup.sh

# Option 2: Direct setup script
bash scripts/setup-dev-tools.sh
```

Auto-run on folder open (same tasks as Windows).

### Linux

```bash
# Option 1: Wrapper script (auto-detects OS)
bash scripts/setup.sh

# Option 2: Direct setup script  (assumes apt/dnf/yum available)
bash scripts/setup-dev-tools.sh
```

Auto-run on folder open (same tasks as Windows).

## SQL Server Extension Profile Behavior

- Connection profile automation targets extension `ms-mssql.mssql`.
- Profile name: `mssql-container`.
- Storage location: **VS Code User Settings** (`mssql.connections`) so the connection appears in the extension across workspaces.
- Hostname is pinned to `127.0.0.1` (avoids localhost/IPv6 ambiguity on some hosts).
- Port selection is dynamic when possible:
   - preferred: live Docker mapping from running `sql-*` container (`hostPort -> 1433`)
   - fallback: `SQL_HOST_PORT` from `src/aspire/.env`
- If SQL host port changes after restarting Aspire, run task `setup: ensure SQL Server connection profile` again.

## What Gets Installed

### Automatic / Verified

| Component | Windows | macOS | Linux |
|-----------|---------|-------|-------|
| **Dapr CLI** | User home | Homebrew | curl |
| **Azure CLI** | Verified; manual link if missing | Homebrew | apt/dnf/yum |
| **azd** | Verified; manual link if missing | curl | curl |
| **VS Code Extensions** | PowerShell | bash | bash |
| **.NET restore** | ✓ | ✓ | ✓ |
| **npm ci** | ✓ | ✓ | ✓ |
| **Architecture tests** | ✓ | ✓ | ✓ |

### What Might Need Manual Installation

- **Docker Desktop** — Must be installed and running manually before setup
- **Shellcheck** (optional) — Used only for CI/CD linting; setup will prompt

## Troubleshooting

### Windows

**"Unable to locate the Dapr CLI" on F5**
- Run `scripts/setup.bat` or `powershell.exe -ExecutionPolicy Bypass -File scripts/setup-env.ps1`
- Dapr CLI will be installed to `%USERPROFILE%\.dapr\bin` and added to PATH
- Restart VS Code after install completes

**PowerShell script execution blocked**
- Run in PowerShell: `Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser`
- Or use: `powershell.exe -ExecutionPolicy Bypass -File scripts/setup-env.ps1`

**"code" CLI not found when installing extensions**
- In VS Code on Windows: `Ctrl+Shift+P` → `"Shell Command: Install 'code' command in PATH"`
- Then retry setup

**MSSQL extension timeout / Error 258**
- Re-run task `setup: ensure SQL Server connection profile` (updates profile to current Docker SQL host port)
- In MSSQL Connections pane, reconnect `mssql-container`
- If needed, remove and re-add the node via `MS SQL: Connect` to clear stale in-memory state

### macOS

**Homebrew prompts for password**
- Enter your macOS login password when prompted (required for package manager)

**shellcheck install fails**
- Set `AUTO_INSTALL_SHELLCHECK=true` before running:
  ```bash
  AUTO_INSTALL_SHELLCHECK=true bash scripts/setup-dev-tools.sh
  ```

### Linux

**apt/dnf/yum permission required**
- Some package manager operations require `sudo` (e.g., shellcheck install)
- Script will prompt when needed
- To skip interactive prompts: `VALIDATE_ONLY=true bash scripts/setup-dev-tools.sh`

**Code CLI not in PATH**
- Ensure VS Code is installed and `/usr/bin/code` exists
- Or install from Microsoft repos: https://code.visualstudio.com/docs/setup/linux

## After Setup

1. **Open the workspace in VS Code:** folder-open tasks create `src/aspire/.env` from `src/aspire/.env.example` when needed and preserve existing non-empty custom values.

2. **Press F5** in VS Code to debug the full local stack
   - Aspire orchestrates: frontend, backend, worker, SQL, Redis, Qdrant, Dapr

3. **Deploy to Azure:**
   - Follow: [GitHub Secrets Setup](../docs/GitHub-Secrets-Setup.md#quick-bootstrap-checklist-5-10-min)
   - Push to `main` → GitHub Actions handles everything

## Key Setup Scripts

| Script | Platform | Purpose |
|--------|----------|---------|
| `scripts/setup.sh` | macOS/Linux/WSL | Auto-detect OS wrapper |
| `scripts/setup.bat` | Windows | Batch entry point |
| `scripts/setup-env.ps1` | Windows | PowerShell setup (full) |
| `scripts/setup-dev-tools.sh` | macOS/Linux | Bash setup (full) |
| `.vscode/install-extensions.sh` | macOS/Linux | Install VS Code extensions |
| `.vscode/install-extensions.ps1` | Windows | Install VS Code extensions |

## Environment Variables

**For CI/validation only (skip installs):**
```bash
# Bash
VALIDATE_ONLY=true bash scripts/setup-dev-tools.sh

# PowerShell
powershell.exe -ExecutionPolicy Bypass -File scripts/setup-env.ps1 -ValidateOnly
```

PowerShell-only form:

```powershell
$env:VALIDATE_ONLY = "true"
powershell.exe -ExecutionPolicy Bypass -File scripts/setup-env.ps1
```

**For Dapr components (already set by AppHost.cs):**
```bash
cp src/aspire/.env.example src/aspire/.env
# Dapr components are in src/components/ (repo-scoped, not ~/.dapr)
```

```powershell
Copy-Item src/aspire/.env.example src/aspire/.env
# Dapr components are in src/components/ (repo-scoped, not ~/.dapr)
```

## Notes

- **No admin rights required** — All installations target user directories
- **Idempotent** — Safe to run setup multiple times
- **VS Code tasks auto-run** — On folder open, tasks silently restore dependencies
- **Dev Container** — If using `.devcontainer/`, these scripts run automatically inside the container
