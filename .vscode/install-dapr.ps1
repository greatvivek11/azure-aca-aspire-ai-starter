# Ensure Dapr CLI and runtime are available for Aspire sidecars.
# Idempotent: safe to run repeatedly on folder open.

$ErrorActionPreference = "Stop"

function Write-Info {
    param([string]$Message)
    Write-Host "[info] $Message"
}

function Write-Warn {
    param([string]$Message)
    Write-Host "[warn] $Message"
}

function Add-DirToPath {
    param([string]$Dir)

    if (-not (Test-Path $Dir)) {
        return
    }

    $userPath = [Environment]::GetEnvironmentVariable("PATH", "User")
    if ($userPath -notlike "*$Dir*") {
        [Environment]::SetEnvironmentVariable("PATH", "$Dir;$userPath", "User")
    }

    if ($env:PATH -notlike "*$Dir*") {
        $env:PATH = "$Dir;$env:PATH"
    }
}

function Get-DaprExePath {
    $candidates = @(
        "C:\dapr\dapr.exe",
        (Join-Path $env:USERPROFILE ".dapr\bin\dapr.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
}

function Ensure-DaprCli {
    $daprExe = Get-DaprExePath
    if ($daprExe) {
        Add-DirToPath (Split-Path -Parent $daprExe)
        Write-Info "Dapr CLI present at $daprExe"
        return $true
    }

    Write-Info "Running official Dapr installer..."
    Invoke-Expression ((Invoke-WebRequest -UseBasicParsing -Uri "https://raw.githubusercontent.com/dapr/cli/master/install/install.ps1").Content)

    $daprExe = Get-DaprExePath
    if ($daprExe) {
        Add-DirToPath (Split-Path -Parent $daprExe)
        Write-Info "Dapr CLI installed at $daprExe"
        return $true
    }

    return $false
}

function Ensure-DaprRuntime {
    $daprdExe = Join-Path $env:USERPROFILE ".dapr\bin\daprd.exe"
    if (Test-Path $daprdExe) {
        Write-Info "Dapr runtime already present at $daprdExe"
        return $true
    }

    Write-Info "Dapr runtime missing. Running 'dapr init --slim'..."
    & dapr init --slim

    if (Test-Path $daprdExe) {
        Write-Info "Dapr runtime installed at $daprdExe"
        return $true
    }

    return $false
}

if (-not (Ensure-DaprCli)) {
    Write-Warn "Dapr CLI installation did not complete successfully."
    exit 1
}

if (-not (Get-Command dapr -ErrorAction SilentlyContinue)) {
    Write-Warn "Dapr CLI is installed but not available in current shell PATH. Restart VS Code terminal."
    exit 1
}

if (-not (Ensure-DaprRuntime)) {
    Write-Warn "Dapr runtime install failed. Run 'dapr init --slim' manually and retry."
    exit 1
}

$version = dapr --version
Write-Info "Dapr is ready.`n$version"
exit 0
