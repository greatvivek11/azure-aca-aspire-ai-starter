# Install and verify development tools: .NET, Node.js, Docker, Dapr, Azure CLI, azd
# Windows-native PowerShell version (no admin rights required)
# Usage: .\scripts\setup-env.ps1
# For CI/validation-only: .\scripts\setup-env.ps1 -ValidateOnly

param(
    [switch]$ValidateOnly = $false,
    [switch]$Verbose = $false
)

$ErrorActionPreference = "Stop"
$WarningPreference = "Continue"

function Write-Info {
    param([string]$Message)
    Write-Host "[setup] $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host "[setup] $Message" -ForegroundColor Yellow
}

function Write-Err {
    param([string]$Message)
    Write-Host "[setup] $Message" -ForegroundColor Red
}

function Compare-Version {
    param(
        [string]$CurrentVersion,
        [string]$RequiredVersion
    )
    try {
        $current = [version]$CurrentVersion
        $required = [version]$RequiredVersion
        return $current -ge $required
    } catch {
        return $false
    }
}

function Get-ProgramInPath {
    param([string]$Program)
    return $null -ne (Get-Command $Program -ErrorAction SilentlyContinue)
}

function Install-DaprCli {
    if (Get-ProgramInPath dapr) {
        $version = dapr --version | Select-Object -First 1
        Write-Info "Dapr CLI already installed: $version"
        return $true
    }

        Write-Info "Dapr CLI not found; attempting installation..."

    try {
        $daprInstallDir = Join-Path $ENV:USERPROFILE ".dapr\bin"

        if (-not (Test-Path $daprInstallDir)) {
            New-Item -ItemType Directory -Path $daprInstallDir -Force | Out-Null
        }

        Write-Info "Downloading Dapr CLI installer..."
        $tmpDir = [System.IO.Path]::GetTempPath()
        $installerScript = Join-Path $tmpDir "dapr-install.ps1"
        $installerUrl = "https://raw.githubusercontent.com/dapr/cli/master/install/install.ps1"

        Invoke-WebRequest -Uri $installerUrl -OutFile $installerScript -UseBasicParsing -ErrorAction Stop

        Write-Info "Running Dapr installer..."
        & powershell -ExecutionPolicy Bypass -File $installerScript -Quiet

        # Add to PATH if needed
        $pathEnv = [Environment]::GetEnvironmentVariable("PATH", "User")
        if ($pathEnv -notlike "*$daprInstallDir*") {
            Write-Info "Adding Dapr to user PATH..."
            $newPath = if ([string]::IsNullOrWhiteSpace($pathEnv)) { $daprInstallDir } else { "$daprInstallDir;$pathEnv" }
            [Environment]::SetEnvironmentVariable("PATH", $newPath, "User")
        }

        if ($ENV:PATH -notlike "*$daprInstallDir*") {
            $ENV:PATH = "$daprInstallDir;$ENV:PATH"
        }

        if (Get-ProgramInPath dapr) {
            $version = dapr --version | Select-Object -First 1
            Write-Info "Dapr CLI installed: $version"
            return $true
        } else {
            Write-Warn "Dapr install attempted; restart terminal to refresh PATH"
            return $false
        }
    } catch {
        Write-Err "Failed to install Dapr CLI: $_"
        Write-Warn "Install manually: https://docs.dapr.io/getting-started/install-dapr-cli/"
        return $false
    }
}

function Install-AzureCli {
    if (Get-ProgramInPath az) {
        $version = az --version 2>&1 | Select-Object -First 1
        Write-Info "Azure CLI already installed"
        return $true
    }

    Write-Info "Azure CLI not found; download from https://aka.ms/InstallAzureCliBundledWindows"
    return $false
}

function Install-Azd {
    if (Get-ProgramInPath azd) {
        Write-Info "Azure Developer CLI (azd) already installed"
        return $true
    }

    Write-Info "azd not found; download from https://aka.ms/install-azd.ps1"
    return $false
}

# Resolve script directory
$scriptPath = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$rootDir = Split-Path -Parent $scriptPath

Write-Info "Repository root: $rootDir"
Write-Info "Windows PowerShell setup starting..."

# Validate .NET SDK
if (-not (Get-ProgramInPath dotnet)) {
    Write-Err ".NET SDK is required. Install from https://dotnet.microsoft.com/download"
    exit 1
}

$dotnetVersion = (dotnet --version 2>&1) -split '\s+' | Select-Object -First 1
if (-not (Compare-Version $dotnetVersion "10.0.0")) {
    Write-Err ".NET 10.0+ is required (found: $dotnetVersion)"
    exit 1
}
Write-Info "dotnet version: $dotnetVersion"

# Validate Node.js
if (-not (Get-ProgramInPath node)) {
    Write-Err "Node.js is required. Install from https://nodejs.org"
    exit 1
}

$nodeVersion = node --version
$nodeVersionNumber = $nodeVersion -replace 'v', ''
if (-not (Compare-Version $nodeVersionNumber "20.0.0")) {
    Write-Err "Node.js 20+ is required (found: $nodeVersion)"
    exit 1
}
Write-Info "node version: $nodeVersion"

# Validate npm
if (-not (Get-ProgramInPath npm)) {
    Write-Err "npm is required. Install Node.js from https://nodejs.org"
    exit 1
}

$npmVersion = npm --version
if (-not (Compare-Version $npmVersion "10.0.0")) {
    Write-Err "npm 10.0+ is required (found: $npmVersion)"
    exit 1
}
Write-Info "npm version: $npmVersion"

# Validate Docker
if (-not (Get-ProgramInPath docker)) {
    Write-Warn "Docker not found (optional: install from https://www.docker.com/products/docker-desktop)"
} else {
    $dockerVersion = docker --version
    Write-Info "docker version: $dockerVersion"
}

if ($ValidateOnly) {
    Write-Info "Validation mode: exiting (skipping installs)"
    exit 0
}

# Install tools
Write-Info "Installing/verifying development tools..."
$daprOk = Install-DaprCli
$azureOk = Install-AzureCli
$azdOk = Install-Azd

# Restore .NET solution
Write-Info "Restoring .NET solution..."
try {
    & dotnet restore "$rootDir\azure-aca-aspire-ai-starter.sln"
} catch {
    Write-Err "Failed to restore solution: $_"
    exit 1
}

# Install frontend
Write-Info "Installing frontend dependencies..."
try {
    Push-Location "$rootDir\src\frontend"
    & npm ci
    Pop-Location
} catch {
    Write-Err "Failed to install frontend: $_"
    exit 1
}

# Lint frontend
Write-Info "Running frontend lint..."
try {
    Push-Location "$rootDir\src\frontend"
    & npm run lint
    Pop-Location
} catch {
    Write-Warn "Frontend lint issues (non-fatal)"
}

# Run backend tests
Write-Info "Running backend tests..."
try {
    & dotnet test "$rootDir\src\Backend.Tests\Backend.Tests.csproj"
} catch {
    Write-Err "Backend tests failed: $_"
    exit 1
}

Write-Info "Setup completed successfully!"
exit 0
