# Ensure Docker Desktop engine is running on folder-open.
# Idempotent: safe to run repeatedly.

$ErrorActionPreference = "Stop"

function Write-Info {
    param([string]$Message)
    Write-Host "[info] $Message"
}

function Write-Warn {
    param([string]$Message)
    Write-Host "[warn] $Message"
}

function Test-DockerEngine {
    try {
        docker info *> $null
        return $LASTEXITCODE -eq 0
    }
    catch {
        return $false
    }
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Warn "Docker CLI is not installed. Install Docker Desktop to run local containers."
    exit 1
}

if (Test-DockerEngine) {
    Write-Info "Docker engine is already running."
    exit 0
}

$dockerDesktopExe = "C:\Program Files\Docker\Docker\Docker Desktop.exe"
if (-not (Test-Path $dockerDesktopExe)) {
    Write-Warn "Docker Desktop is not installed at '$dockerDesktopExe'."
    exit 1
}

Write-Info "Starting Docker Desktop..."
Start-Process -FilePath $dockerDesktopExe | Out-Null

for ($attempt = 1; $attempt -le 90; $attempt++) {
    Start-Sleep -Seconds 2
    if (Test-DockerEngine) {
        Write-Info "Docker engine is ready."
        exit 0
    }
}

Write-Warn "Docker Desktop started but engine did not become ready within 3 minutes."
exit 1
