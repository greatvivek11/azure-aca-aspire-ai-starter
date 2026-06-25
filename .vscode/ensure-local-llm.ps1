# Ensure native llama.cpp and local GGUF model files are available.
# This script downloads models, installs a native llama-server release when needed,
# and starts chat + embedding servers for the Aspire containers to call.

param(
    [switch]$SkipModelDownload = $false,
    [switch]$ValidateOnly = $false
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Write-Info {
    param([string]$Message)
    Write-Host "[local-llm] $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host "[local-llm] $Message" -ForegroundColor Yellow
}

function Write-Err {
    param([string]$Message)
    Write-Host "[local-llm] $Message" -ForegroundColor Red
}

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$envPath = Join-Path $workspaceRoot "src/aspire/.env"

function Import-DotEnv {
    if (-not (Test-Path $envPath)) {
        return
    }

    foreach ($line in Get-Content -Path $envPath) {
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

function Get-EnvOrDefault {
    param([string]$Name, [string]$DefaultValue)
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $DefaultValue
    }

    return $value.Trim()
}

function Get-PortFromUrl {
    param([string]$Url, [int]$DefaultPort)
    try {
        $uri = [Uri]$Url
        if ($uri.Port -gt 0) {
            return $uri.Port
        }
    } catch {
        Write-Warn "Could not parse port from '$Url'; using $DefaultPort."
    }

    return $DefaultPort
}

Import-DotEnv

$modelsDir = Get-EnvOrDefault "LLAMA_CPP_MODELS_DIR" (Join-Path $env:USERPROFILE ".local\share\llama.cpp\models")
$binDir = Get-EnvOrDefault "LLAMA_CPP_BIN_DIR" (Join-Path $env:USERPROFILE ".local\share\llama.cpp\bin")
$chatBaseUrl = Get-EnvOrDefault "LLAMA_CPP_BASE_URL" "http://host.docker.internal:8082"
$embedBaseUrl = Get-EnvOrDefault "LLAMA_CPP_EMBED_BASE_URL" "http://host.docker.internal:8083"
$chatPort = Get-PortFromUrl $chatBaseUrl 8082
$embedPort = Get-PortFromUrl $embedBaseUrl 8083
$chatModel = Get-EnvOrDefault "LLAMA_CPP_CHAT_MODEL" "Qwen/Qwen2.5-0.5B-Instruct"
$embedModel = Get-EnvOrDefault "LLAMA_CPP_EMBED_MODEL" "nomic-embed-text"
$gpuLayers = Get-EnvOrDefault "LLAMA_CPP_GPU_LAYERS" ""
$serverPathOverride = Get-EnvOrDefault "LLAMA_CPP_SERVER_PATH" ""
$hfToken = Get-EnvOrDefault "HF_TOKEN" ""
$logDir = Join-Path $workspaceRoot ".vscode\llama-logs"

$models = [ordered]@{
    $chatModel = @{
        Urls = @(
            "https://huggingface.co/bartowski/Qwen2.5-0.5B-Instruct-GGUF/resolve/main/Qwen2.5-0.5B-Instruct-Q4_K_M.gguf?download=true",
            "https://huggingface.co/bartowski/Qwen2.5-0.5B-Instruct-GGUF/resolve/main/Qwen2.5-0.5B-Instruct-Q4_K_M.gguf"
        )
        SizeMb = 398
        MinSizeMb = 300
        File = Get-EnvOrDefault "LLAMA_CPP_CHAT_MODEL_FILE" "Qwen2.5-0.5B-Instruct-Q4_K_M.gguf"
    }
    $embedModel = @{
        Urls = @(
            "https://huggingface.co/nomic-ai/nomic-embed-text-v1.5-GGUF/resolve/main/nomic-embed-text-v1.5.f16.gguf?download=true",
            "https://huggingface.co/nomic-ai/nomic-embed-text-v1.5-GGUF/resolve/main/nomic-embed-text-v1.5.f16.gguf"
        )
        SizeMb = 274
        MinSizeMb = 200
        File = Get-EnvOrDefault "LLAMA_CPP_EMBED_MODEL_FILE" "nomic-embed-text-v1.5.f16.gguf"
    }
}

$chatFallbackCandidates = @(
    @{
        Name = "unsloth/gemma-3-1b-it-GGUF"
        File = "gemma-3-1b-it-Q4_K_M.gguf"
        SizeMb = 806
        MinSizeMb = 600
        Urls = @(
            "https://huggingface.co/unsloth/gemma-3-1b-it-GGUF/resolve/main/gemma-3-1b-it-Q4_K_M.gguf?download=true",
            "https://huggingface.co/unsloth/gemma-3-1b-it-GGUF/resolve/main/gemma-3-1b-it-Q4_K_M.gguf"
        )
    },
    @{
        Name = "meta-llama/Llama-3.2-1B-Instruct"
        File = "Llama-3.2-1B-Instruct-Q4_K_M.gguf"
        SizeMb = 808
        MinSizeMb = 600
        Urls = @(
            "https://huggingface.co/bartowski/Llama-3.2-1B-Instruct-GGUF/resolve/main/Llama-3.2-1B-Instruct-Q4_K_M.gguf?download=true",
            "https://huggingface.co/bartowski/Llama-3.2-1B-Instruct-GGUF/resolve/main/Llama-3.2-1B-Instruct-Q4_K_M.gguf"
        )
    },
    @{
        Name = "Qwen/Qwen2.5-1.5B-Instruct"
        File = "Qwen2.5-1.5B-Instruct-Q4_K_M.gguf"
        SizeMb = 986
        MinSizeMb = 700
        Urls = @(
            "https://huggingface.co/bartowski/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/Qwen2.5-1.5B-Instruct-Q4_K_M.gguf?download=true",
            "https://huggingface.co/bartowski/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/Qwen2.5-1.5B-Instruct-Q4_K_M.gguf"
        )
    }
)

$resolvedChatModelName = $chatModel
$resolvedChatModelFile = $models[$chatModel].File

function Set-DotEnvValue {
    param(
        [string]$Key,
        [string]$Value
    )

    $lines = @()
    if (Test-Path $envPath) {
        $lines = Get-Content -Path $envPath
    }

    $pattern = "^\s*" + [Regex]::Escape($Key) + "\s*="
    $updated = $false

    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match $pattern) {
            $lines[$index] = "$Key=$Value"
            $updated = $true
            break
        }
    }

    if (-not $updated) {
        $lines += "$Key=$Value"
    }

    Set-Content -Path $envPath -Value $lines
}

function Persist-ResolvedChatModel {
    if (($resolvedChatModelName -eq $chatModel) -and ($resolvedChatModelFile -eq $models[$chatModel].File)) {
        return
    }

    Set-DotEnvValue -Key "LLAMA_CPP_CHAT_MODEL" -Value $resolvedChatModelName
    Set-DotEnvValue -Key "LLAMA_CPP_CHAT_MODEL_FILE" -Value $resolvedChatModelFile
    Write-Warn "Persisted fallback chat model to .env: $resolvedChatModelName ($resolvedChatModelFile)"
}

function Test-ModelFileAvailable {
    param(
        [string]$ModelLabel,
        [string]$ModelFile,
        [double]$MinSizeMb = 1,
        [switch]$DeleteIfTooSmall
    )

    $localPath = Join-Path $modelsDir $ModelFile
    if (-not (Test-Path $localPath)) {
        return $false
    }

    $size = (Get-Item $localPath).Length / 1MB
    if ($size -lt $MinSizeMb) {
        Write-Warn "Model file appears incomplete for $ModelLabel ($([math]::Round($size, 1)) MB < expected minimum $MinSizeMb MB): $localPath"
        if ($DeleteIfTooSmall) {
            Remove-Item $localPath -Force -ErrorAction SilentlyContinue
            Write-Warn "Removed incomplete model file: $localPath"
        }

        return $false
    }

    Write-Info "Model available: $ModelLabel ($([math]::Round($size, 1)) MB)"
    return $true
}

function Download-ModelFile {
    param(
        [string]$ModelName,
        [string[]]$Urls,
        [string]$LocalPath,
        [double]$MinSizeMb = 1
    )

    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    $headers = @{}
    if (-not [string]::IsNullOrWhiteSpace($hfToken)) {
        $headers["Authorization"] = "Bearer $hfToken"
    }

    $lastError = $null
    foreach ($url in $Urls) {
        Write-Info "Attempting download for $ModelName from $url"
        try {
            if ($curl) {
                $args = @(
                    "-fL",
                    "--retry", "6",
                    "--retry-all-errors",
                    "-A", "azure-aca-aspire-ai-starter-local-setup/1.0",
                    "-C", "-",
                    "-o", $LocalPath,
                    $url
                )

                if ($headers.ContainsKey("Authorization")) {
                    $args = @("-H", "Authorization: Bearer $hfToken") + $args
                }

                & $curl.Source @args | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    throw "curl exited with code $LASTEXITCODE"
                }
            } else {
                if ($headers.Count -gt 0) {
                    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $LocalPath -MaximumRedirection 10 -UserAgent "azure-aca-aspire-ai-starter-local-setup/1.0" -Headers $headers
                } else {
                    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $LocalPath -MaximumRedirection 10 -UserAgent "azure-aca-aspire-ai-starter-local-setup/1.0"
                }
            }

            if (-not (Test-ModelFileAvailable -ModelLabel $ModelName -ModelFile (Split-Path -Leaf $LocalPath) -MinSizeMb $MinSizeMb)) {
                $lastError = "Downloaded file for $ModelName is smaller than expected minimum ${MinSizeMb}MB"
                continue
            }

            return $true
        } catch {
            $lastError = $_.Exception.Message
        }
    }

    if ($lastError -match "401") {
        Write-Warn "Hugging Face returned 401 for $ModelName. If your network or account requires auth, set HF_TOKEN and rerun setup."
    }

    Write-Err "Failed to download $ModelName from all candidate URLs: $lastError"
    return $false
}

function Test-ModelAvailable {
    param([string]$ModelName)

    $modelInfo = $models[$ModelName]
    return Test-ModelFileAvailable -ModelLabel $ModelName -ModelFile $modelInfo.File -MinSizeMb $modelInfo.MinSizeMb
}

function Save-Model {
    param([string]$ModelName)

    if ($ModelName -eq $chatModel) {
        $primary = $models[$ModelName]
        if (Test-ModelFileAvailable -ModelLabel $ModelName -ModelFile $primary.File -MinSizeMb $primary.MinSizeMb -DeleteIfTooSmall) {
            $script:resolvedChatModelName = $ModelName
            $script:resolvedChatModelFile = $primary.File
            return $true
        }

        if (-not $SkipModelDownload) {
            New-Item -ItemType Directory -Path $modelsDir -Force | Out-Null
            Write-Info "Downloading $ModelName (~$($primary.SizeMb) MB) to $(Join-Path $modelsDir $primary.File)"
            if (Download-ModelFile -ModelName $ModelName -Urls $primary.Urls -LocalPath (Join-Path $modelsDir $primary.File) -MinSizeMb $primary.MinSizeMb) {
                $script:resolvedChatModelName = $ModelName
                $script:resolvedChatModelFile = $primary.File
                return Test-ModelFileAvailable -ModelLabel $ModelName -ModelFile $primary.File -MinSizeMb $primary.MinSizeMb
            }
        }

        Write-Warn "Primary chat model '$ModelName' is unavailable. Trying fallback chat models under 1GB..."
        foreach ($candidate in $chatFallbackCandidates) {
            if (Test-ModelFileAvailable -ModelLabel $candidate.Name -ModelFile $candidate.File -MinSizeMb $candidate.MinSizeMb -DeleteIfTooSmall) {
                Write-Warn "Using fallback chat model file: $($candidate.File)"
                $script:resolvedChatModelName = $candidate.Name
                $script:resolvedChatModelFile = $candidate.File
                return $true
            }

            if ($SkipModelDownload) {
                continue
            }

            Write-Info "Downloading fallback $($candidate.Name) (~$($candidate.SizeMb) MB) to $(Join-Path $modelsDir $candidate.File)"
            if (Download-ModelFile -ModelName $candidate.Name -Urls $candidate.Urls -LocalPath (Join-Path $modelsDir $candidate.File) -MinSizeMb $candidate.MinSizeMb) {
                Write-Warn "Downloaded fallback chat model: $($candidate.Name)"
                $script:resolvedChatModelName = $candidate.Name
                $script:resolvedChatModelFile = $candidate.File
                return Test-ModelFileAvailable -ModelLabel $candidate.Name -ModelFile $candidate.File -MinSizeMb $candidate.MinSizeMb
            }
        }

        if ($SkipModelDownload) {
            Write-Warn "Model download skipped; no chat model files available in cache."
        }

        return $false
    }

    if (Test-ModelAvailable $ModelName) {
        return $true
    }

    $modelInfo = $models[$ModelName]
    $localPath = Join-Path $modelsDir $modelInfo.File

    if ($SkipModelDownload) {
        Write-Warn "Model download skipped; missing $ModelName at $localPath"
        return $false
    }

    New-Item -ItemType Directory -Path $modelsDir -Force | Out-Null
    Write-Info "Downloading $ModelName (~$($modelInfo.SizeMb) MB) to $localPath"

    if (-not (Download-ModelFile -ModelName $ModelName -Urls $modelInfo.Urls -LocalPath $localPath -MinSizeMb $modelInfo.MinSizeMb)) {
        return $false
    }

    return Test-ModelAvailable $ModelName
}

function Find-LlamaServer {
    param([string]$SearchRoot)

    if (-not [string]::IsNullOrWhiteSpace($serverPathOverride) -and (Test-Path $serverPathOverride)) {
        return (Resolve-Path $serverPathOverride).Path
    }

    if (-not (Test-Path $SearchRoot)) {
        return $null
    }

    $match = Get-ChildItem -Path $SearchRoot -Filter "llama-server.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($match) {
        return $match.FullName
    }

    return $null
}

function Select-LlamaAsset {
    param($Assets)

    $patterns = @(
        "bin-win-vulkan-x64\.zip$",
        "bin-win-sycl-x64\.zip$",
        "bin-win-cpu-x64\.zip$",
        "win.*x64.*\.zip$"
    )

    foreach ($pattern in $patterns) {
        $asset = $Assets | Where-Object { $_.name -match $pattern } | Select-Object -First 1
        if ($asset) {
            return $asset
        }
    }

    return $null
}

function Install-LlamaCpp {
    $existing = Find-LlamaServer $binDir
    if ($existing) {
        Write-Info "llama-server available: $existing"
        return $existing
    }

    New-Item -ItemType Directory -Path $binDir -Force | Out-Null
    Write-Info "Downloading native llama.cpp release for Windows x64"

    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/ggml-org/llama.cpp/releases/latest" -Headers @{ "User-Agent" = "azure-aca-aspire-ai-starter-setup" }
    $asset = Select-LlamaAsset $release.assets
    if (-not $asset) {
        throw "Could not find a Windows x64 llama.cpp release asset. Set LLAMA_CPP_SERVER_PATH to an existing llama-server.exe."
    }

    $archivePath = Join-Path $binDir $asset.name
    $installRoot = Join-Path $binDir "native"

    Write-Info "Downloading $($asset.name)"
    $webClient = New-Object System.Net.WebClient
    $webClient.DownloadFile($asset.browser_download_url, $archivePath)

    if (Test-Path $installRoot) {
        Remove-Item $installRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
    Expand-Archive -Path $archivePath -DestinationPath $installRoot -Force

    $serverPath = Find-LlamaServer $installRoot
    if (-not $serverPath) {
        throw "Downloaded llama.cpp release did not contain llama-server.exe. Set LLAMA_CPP_SERVER_PATH manually."
    }

    Write-Info "Installed llama-server: $serverPath"
    return $serverPath
}

function Test-ServerReady {
    param([int]$Port)

    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$Port/health" -TimeoutSec 2
        return $response.StatusCode -ge 200 -and $response.StatusCode -lt 500
    } catch {
        return $false
    }
}

function Start-LlamaServer {
    param(
        [string]$Name,
        [int]$Port,
        [string]$ModelFile,
        [string]$Alias,
        [switch]$Embedding,
        [string]$ServerPath
    )

    if (Test-ServerReady $Port) {
        Write-Info "$Name already responding on port $Port"
        return $true
    }

    $modelPath = Join-Path $modelsDir $ModelFile
    if (-not (Test-Path $modelPath)) {
        Write-Err "Missing model file for $Name`: $modelPath"
        return $false
    }

    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    $stdoutLog = Join-Path $logDir "$Name.out.log"
    $stderrLog = Join-Path $logDir "$Name.err.log"
    $pidPath = Join-Path $logDir "$Name.pid"

    $serverArgs = @(
        "--host", "0.0.0.0",
        "--port", "$Port",
        "--model", $modelPath,
        "--alias", $Alias
    )

    if ($Embedding) {
        $serverArgs += "--embedding"
    }

    if (-not [string]::IsNullOrWhiteSpace($gpuLayers)) {
        $serverArgs += @("--n-gpu-layers", $gpuLayers)
    }

    Write-Info "Starting native $Name on port $Port"
    $process = Start-Process -FilePath $ServerPath -ArgumentList $serverArgs -WorkingDirectory (Split-Path -Parent $ServerPath) -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog -WindowStyle Hidden -PassThru
    Set-Content -Path $pidPath -Value $process.Id

    for ($attempt = 1; $attempt -le 90; $attempt++) {
        Start-Sleep -Seconds 2
        if (Test-ServerReady $Port) {
            Write-Info "$Name is ready on port $Port"
            return $true
        }

        if ($process.HasExited) {
            Write-Err "$Name exited early. Check $stderrLog"
            return $false
        }
    }

    Write-Err "$Name did not become ready within 3 minutes. Check $stderrLog"
    return $false
}

Write-Info "Ensuring native llama.cpp setup"
Write-Info "Model cache: $modelsDir"
Write-Info "Binary cache: $binDir"

if ($ValidateOnly) {
    if (-not (Test-ModelFileAvailable -ModelLabel $chatModel -ModelFile $models[$chatModel].File -MinSizeMb $models[$chatModel].MinSizeMb)) {
        foreach ($candidate in $chatFallbackCandidates) {
            if (Test-ModelFileAvailable -ModelLabel $candidate.Name -ModelFile $candidate.File -MinSizeMb $candidate.MinSizeMb) {
                break
            }
        }
    }
    [void](Test-ModelAvailable $embedModel)

    [void](Find-LlamaServer $binDir)
    Write-Info "Validation complete; downloads and server startup skipped."
    exit 0
}

$allReady = $true
foreach ($modelName in $models.Keys) {
    if (-not (Save-Model $modelName)) {
        $allReady = $false
    }
}

try {
    $serverPath = Install-LlamaCpp
} catch {
    Write-Err $_.Exception.Message
    $allReady = $false
}

if ($allReady) {
    Persist-ResolvedChatModel
    $chatFile = $resolvedChatModelFile
    $embedFile = $models[$embedModel].File
    if (-not (Start-LlamaServer -Name "llama-chat" -Port $chatPort -ModelFile $chatFile -Alias $resolvedChatModelName -ServerPath $serverPath)) {
        $allReady = $false
    }

    if (-not (Start-LlamaServer -Name "llama-embed" -Port $embedPort -ModelFile $embedFile -Alias $embedModel -Embedding -ServerPath $serverPath)) {
        $allReady = $false
    }
}

if (-not $allReady) {
    Write-Err "Native local AI setup did not complete."
    exit 1
}

Write-Info "Native llama.cpp chat and embedding servers are ready."
exit 0
