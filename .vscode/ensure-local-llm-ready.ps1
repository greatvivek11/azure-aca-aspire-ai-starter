# Verify native llama.cpp chat and embedding endpoints are healthy.
# Intended for pre-launch readiness checks after setup tasks start local servers.

$ErrorActionPreference = "Stop"

function Write-Info {
    param([string]$Message)
    Write-Host "[local-llm-ready] $Message"
}

function Get-PortFromUrl {
    param([string]$Url, [int]$DefaultPort)

    try {
        $uri = [Uri]$Url
        if ($uri.Port -gt 0) {
            return $uri.Port
        }
    } catch {
        Write-Info "Could not parse '$Url'; using fallback port $DefaultPort"
    }

    return $DefaultPort
}

function Load-DotEnv {
    param([string]$FilePath)

    if (-not (Test-Path $FilePath)) {
        return
    }

    foreach ($line in Get-Content -Path $FilePath) {
        if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith("#")) {
            continue
        }

        $parts = $line.Split('=', 2)
        if ($parts.Count -ne 2) {
            continue
        }

        $key = $parts[0].Trim()
        $value = $parts[1].Trim()
        if (-not [string]::IsNullOrWhiteSpace($key) -and [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($key))) {
            [Environment]::SetEnvironmentVariable($key, $value, "Process")
        }
    }
}

function Wait-Endpoint {
    param(
        [string]$Name,
        [int]$Port,
        [int]$Attempts = 90,
        [int]$DelaySeconds = 2
    )

    $url = "http://127.0.0.1:$Port/health"
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 2
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                Write-Info "$Name is healthy at $url"
                return $true
            }
        } catch {
            # Continue until timeout.
        }

        Start-Sleep -Seconds $DelaySeconds
    }

    Write-Error "Timed out waiting for $Name at $url"
    return $false
}

function Get-EnvOrDefault {
    param([string]$Name, [string]$DefaultValue)

    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $DefaultValue
    }

    return $value.Trim()
}

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$envPath = Join-Path $workspaceRoot "src/aspire/.env"
Load-DotEnv -FilePath $envPath

$chatPort = Get-PortFromUrl -Url (Get-EnvOrDefault -Name "LLAMA_CPP_BASE_URL" -DefaultValue "http://host.docker.internal:8082") -DefaultPort 8082
$embedPort = Get-PortFromUrl -Url (Get-EnvOrDefault -Name "LLAMA_CPP_EMBED_BASE_URL" -DefaultValue "http://host.docker.internal:8083") -DefaultPort 8083

Write-Info "Waiting for native llama.cpp endpoints (chat:$chatPort, embed:$embedPort)"

$chatReady = Wait-Endpoint -Name "chat endpoint" -Port $chatPort
$embedReady = Wait-Endpoint -Name "embedding endpoint" -Port $embedPort

if (-not ($chatReady -and $embedReady)) {
    exit 1
}

Write-Info "Native llama.cpp readiness checks passed."
exit 0
