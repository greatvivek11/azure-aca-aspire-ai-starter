# Install required VS Code extensions from .vscode/extensions.json (single source of truth).
# This script is idempotent: reinstall attempts are safe.
# Does not require admin rights.

param(
    [switch]$Verbose = $false
)

$ErrorActionPreference = "Continue"
$WarningPreference = "Continue"

# Resolve script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$extensionsJsonPath = Join-Path $scriptDir "extensions.json"

if (-not (Test-Path $extensionsJsonPath)) {
    Write-Error "[error] extensions.json not found at $extensionsJsonPath"
    exit 1
}

# Verify code CLI is available
$codeBin = if (Get-Command code -ErrorAction SilentlyContinue) {
    "code"
} else {
    Write-Error "[error] 'code' CLI not found. Ensure VS Code is installed and 'code' is in PATH."
    Write-Error "[info] You can add VS Code to PATH via: Settings > Install 'code' command in PATH"
    exit 1
}

# Parse extensions from extensions.json
try {
    $extensionsJson = Get-Content $extensionsJsonPath -Raw | ConvertFrom-Json
    $extensions = $extensionsJson.recommendations
} catch {
    Write-Error "[error] Failed to parse extensions.json: $_"
    exit 1
}

if (-not $extensions -or $extensions.Count -eq 0) {
    Write-Error "[error] No extensions found in extensions.json"
    exit 1
}

Write-Host "[info] Installing $($extensions.Count) required VS Code extensions..."

$failedCount = 0
foreach ($ext in $extensions) {
    Write-Host "Installing $ext..."

    # Run code command; capture output to suppress verbose messages
    $output = & $codeBin --install-extension $ext 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Warning "[warn] Failed to install ${ext} (exit code: $LASTEXITCODE)"
        if ($Verbose) {
            Write-Host "Output: $output"
        }
        $failedCount++
    }
}

if ($failedCount -gt 0) {
    Write-Warning "[warn] $failedCount extension(s) failed to install"
    Write-Host "[info] This may happen if VS Code is running; close and retry."
    exit 1
} else {
    Write-Host "[info] Extension bootstrap completed successfully."
    exit 0
}
