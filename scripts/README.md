# Setup Scripts — Overview

This directory contains cross-platform setup scripts for developing the Azure ACA Aspire AI Starter project on Windows, macOS, and Linux.

## Script Ownership Map

- `.vscode/*`:
  - Workspace lifecycle automation (folder-open tasks, F5 prerequisites, editor ergonomics).
  - Example: `.vscode/ensure-aspire-env.ps1` and `.vscode/ensure-aspire-env.sh` ensure `src/aspire/.env` exists and local AI defaults are present.
- `scripts/*`:
  - Developer-invoked setup and validation wrappers intended for local use.
  - Example: `scripts/setup-env.ps1`, `scripts/setup-dev-tools.sh`, `scripts/setup-local-entra-auth.sh`.
- `scripts/ci/*`:
  - CI/CD and cloud bootstrap automation for pipelines and tenant/resource provisioning.
  - Example: `scripts/ci/ensure-entra-auth.sh`, `scripts/ci/bootstrap-ci-tenant-setup.sh`.

Keep these domains separate to preserve single responsibility and minimize coupling between local dev bootstrap and CI identity/provisioning flows.

## Quick Reference

| Platform | Entry Point | Command |
|----------|-------------|---------|
| **Windows** | `scripts/setup.bat` | Double-click or `powershell -ExecutionPolicy Bypass -File scripts/setup-env.ps1` |
| **macOS** | `scripts/setup.sh` | `bash scripts/setup-dev-tools.sh` |
| **Linux** | `scripts/setup.sh` | `bash scripts/setup-dev-tools.sh` |
| **WSL / Git Bash** | `scripts/setup.sh` | `bash scripts/setup-dev-tools.sh` |

## What Each Script Does

### Windows Scripts
- **`scripts/setup-env.ps1`** — Full setup: validates .NET/npm, installs Dapr, verifies Azure CLI/azd, restores deps, runs tests
- **`scripts/setup.bat`** — Batch wrapper for easy double-click execution
- **`.vscode/install-extensions.ps1`** — Installs VS Code extensions from `.vscode/extensions.json`

### Unix Scripts (macOS/Linux)
- **`scripts/setup.sh`** — Auto-detecting wrapper that runs the right script based on OS
- **`scripts/setup-dev-tools.sh`** — Full setup (existing script, now with better error handling)
- **`.vscode/install-extensions.sh`** — Installs VS Code extensions (enhanced with jq fallback)

### Build & Test
- **`.vscode/tasks.json`** — VS Code tasks with OS-specific overrides
  - `setup: install all extensions` — Runs PowerShell on Windows, bash on Unix
  - `npm: install frontend` — Restores frontend deps
  - `dotnet: restore backend/worker` — Restores .NET projects
  - `build` — Builds the solution

## Setup Flow

### Automatic (on folder open)
VS Code automatically runs tasks in this order:
1. Ensure local AI env defaults (`.vscode/ensure-aspire-env.ps1` on Windows, `.sh` on Unix)
2. Install extensions (`.vscode/install-extensions.ps1` on Windows, `.sh` on Unix)
3. Restore frontend + backend + worker
4. Ready for `dotnet build`

Environment variable defaults should be maintained in `src/aspire/.env.example`; bootstrap scripts should mirror that contract rather than inventing parallel defaults.

### Manual (full setup)
```
Windows:  powershell -ExecutionPolicy Bypass -File scripts/setup-env.ps1
Unix:     bash scripts/setup-dev-tools.sh
```

This additionally:
- Installs/verifies Dapr CLI and verifies Azure CLI/azd when present
- Runs frontend linting
- Runs backend architecture tests

## Key Features

✅ **No Admin Rights** — All installations target user home directory
✅ **Idempotent** — Safe to run multiple times
✅ **Cross-Platform** — Same setup experience on Windows, macOS, Linux, WSL
✅ **Validation Mode** — `-ValidateOnly` flag checks deps without installing
✅ **Graceful Fallbacks** — Works even if some optional tools aren't available

## Troubleshooting

### Windows: "Unable to locate the Dapr CLI" on F5
```powershell
powershell -ExecutionPolicy Bypass -File scripts/setup-env.ps1
# Then restart VS Code
```

### Windows: PowerShell execution blocked
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### macOS: Homebrew prompts for password
- Enter your macOS login password when prompted (required for package manager)

### Linux: `apt-get` / `dnf` permission required
- Some installs need `sudo`; you'll be prompted when needed

### Any platform: "code" CLI not in PATH
- **VS Code:** `Ctrl+Shift+P` on Windows/Linux or `Cmd+Shift+P` on macOS → "Shell Command: Install 'code' command in PATH"
- Then retry setup

## Environment Variables

| Variable | Purpose | Default |
|----------|---------|---------|
| `VALIDATE_ONLY` | Skip installs, only check versions | `false` |
| `AUTO_INSTALL_SHELLCHECK` | Auto-install shellcheck on macOS | `false` |
| `DAPR_COMPONENTS_PATH` | Location of Dapr components (set by AppHost.cs) | `src/components` |

**Example:**
```bash
# Bash: Check deps without installing
VALIDATE_ONLY=true bash scripts/setup-dev-tools.sh

# PowerShell: Check deps without installing
powershell -ExecutionPolicy Bypass -File scripts/setup-env.ps1 -ValidateOnly
```

## Documentation

- [Windows Setup (Quick Start)](../docs/WINDOWS-SETUP.md)
- [Cross-Platform Setup Guide](../docs/SETUP-CROSS-PLATFORM.md)
- [Local Development with Dapr](../docs/LOCAL-DEVELOPMENT-DAPR.md)
- [Architecture Tests](../docs/Architecture-Tests.md)

## Files Modified / Created

### New Files
- `scripts/setup-env.ps1` — Windows PowerShell setup
- `scripts/setup.bat` — Windows batch entry point
- `scripts/setup.sh` — Cross-platform wrapper
- `.vscode/install-extensions.ps1` — Windows extension installer
- `docs/WINDOWS-SETUP.md` — Windows quick start
- `docs/SETUP-CROSS-PLATFORM.md` — Full cross-platform guide

### Updated Files
- `.vscode/install-extensions.sh` — Better error handling, jq fallback, code path detection
- `.vscode/tasks.json` — Added Windows PowerShell overrides for setup tasks

## Implementation Details

### Dependency Resolution
- .NET SDK: Required (10.0+)
- Node.js: Required (20+)
- npm: Required (10+)
- Docker: Recommended (not required for some workflows)
- Dapr CLI: Optional (auto-install attempted; required for F5)
- Azure CLI: Optional (install via manual download or package manager)
- azd: Optional (install via manual download or curl)

### PATH Management
- **Windows:** Updates `HKEY_CURRENT_USER\Environment\PATH` (user-level, no admin)
- **macOS/Linux:** Sources from shell environment (bash/zsh)

### Test Coverage
- Backend architecture tests verify Vertical Slice separation
- Frontend lint via ESLint/Biome
- .NET solution restores all projects

## Support

For issues:
1. Run setup script again (scripts are idempotent)
2. Check `.vscode/tasks.json` for task structure
3. Review [docs/SETUP-CROSS-PLATFORM.md](../docs/SETUP-CROSS-PLATFORM.md) for detailed troubleshooting
4. Check repository [README.md](../README.md) for prerequisites
