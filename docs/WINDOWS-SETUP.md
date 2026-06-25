# Windows Setup — Quick Start

## TL;DR

After cloning on Windows:

1. **Open PowerShell** in the repo folder
2. **Run one of:**
   ```powershell
   # Option A: Full setup (takes 2-3 min)
   powershell.exe -ExecutionPolicy Bypass -File scripts/setup-env.ps1

   # Option B: Validation only (checks deps, ~10 sec)
   powershell.exe -ExecutionPolicy Bypass -File scripts/setup-env.ps1 -ValidateOnly

   # Option C: Double-click (uses setup.bat)
   scripts\setup.bat
   ```

3. **Press F5** in VS Code to debug

## What Gets Installed

- ✅ **Dapr CLI** → `%USERPROFILE%\.dapr\bin` (user-scope, no admin)
- ✅ **Dotnet + npm packages** → restored
- ✅ **VS Code extensions** → auto-installed (if offline, run PowerShell script manually)
- ⚠️ **Azure CLI / azd** → Download links provided (optional for local dev)

## Troubleshooting

### "Unable to locate the Dapr CLI" on F5

**Fix:** Run setup script and restart VS Code
```powershell
powershell.exe -ExecutionPolicy Bypass -File scripts/setup-env.ps1
```

Dapr will be installed to `%USERPROFILE%\.dapr\bin` and added to PATH. After install completes:
- Close and reopen VS Code (or restart PowerShell/cmd terminal)
- Try F5 again

### PowerShell execution blocked

**Run this once:**
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

Or always use:
```powershell
powershell.exe -ExecutionPolicy Bypass -File scripts/setup-env.ps1
```

### "code" command not found (during extension install)

**In VS Code:**
1. `Ctrl+Shift+P` → type "Shell Command"
2. Select **"Shell Command: Install 'code' command in PATH"**
3. Retry setup

## Environment Details

| Item | Required | Status |
|------|----------|--------|
| .NET 10+ SDK | Yes | ✅ Check: `dotnet --version` |
| Node.js 20+ | Yes | ✅ Check: `node --version` |
| npm 10+ | Yes | ✅ Check: `npm --version` |
| Docker Desktop | Yes | Must run manually |
| PowerShell 5+ | Yes | Included in Windows |

## Behind the Scenes

The setup script:
1. Validates .NET, Node.js, npm (fails if missing)
2. Installs Dapr CLI to user home (no admin needed)
3. Restores .NET solution
4. Runs `npm ci` for frontend
5. Runs backend architecture tests

All runs are **idempotent** — safe to run multiple times.

## For CI/Automation

```powershell
# Validate only (useful for CI)
$ENV:VALIDATE_ONLY="true"
powershell.exe -ExecutionPolicy Bypass -File scripts/setup-env.ps1

# Or via parameter
powershell.exe -ExecutionPolicy Bypass -File scripts/setup-env.ps1 -ValidateOnly
```

## Enable Local Entra Auth

```powershell
az login
powershell.exe -ExecutionPolicy Bypass -File scripts/setup-local-entra-auth.ps1
```

This creates or reuses local API/SPA app registrations and writes `ENTRA_*` values into `src/aspire/.env`.

## See Also

- [Cross-Platform Setup Guide](../docs/SETUP-CROSS-PLATFORM.md) — macOS, Linux, WSL, dev container
- [README.md](../README.md) — Project overview
- [Quick Deploy Checklist](../docs/GitHub-Secrets-Setup.md) — Deploy to Azure
