# Ensure src/aspire/.env exists and contains local AI defaults.
# Idempotent and non-destructive: existing custom non-empty values are preserved.

$ErrorActionPreference = "Stop"

function Write-Info {
    param([string]$Message)
    Write-Host "[info] $Message"
}

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$envExamplePath = Join-Path $workspaceRoot "src/aspire/.env.example"
$envPath = Join-Path $workspaceRoot "src/aspire/.env"

if (-not (Test-Path $envPath)) {
    if (-not (Test-Path $envExamplePath)) {
        throw "Missing src/aspire/.env.example. Cannot initialize .env file."
    }

    Copy-Item -Path $envExamplePath -Destination $envPath
    Write-Info "Created src/aspire/.env from .env.example"
}

$defaults = [ordered]@{
    "AI_MODE" = "local"
    "LLAMA_CPP_BASE_URL" = "http://host.docker.internal:8082"
    "LLAMA_CPP_EMBED_BASE_URL" = "http://host.docker.internal:8083"
    "LLAMA_CPP_MODELS_DIR" = (Join-Path $env:USERPROFILE ".local\share\llama.cpp\models")
    "LLAMA_CPP_BIN_DIR" = (Join-Path $env:USERPROFILE ".local\share\llama.cpp\bin")
    "LLAMA_CPP_CHAT_MODEL" = "Qwen/Qwen2.5-0.5B-Instruct"
    "LLAMA_CPP_CHAT_MODEL_FILE" = "Qwen2.5-0.5B-Instruct-Q4_K_M.gguf"
    "LLAMA_CPP_EMBED_MODEL" = "nomic-embed-text"
    "LLAMA_CPP_EMBED_MODEL_FILE" = "nomic-embed-text-v1.5.f16.gguf"
    "LLAMA_CPP_EMBED_DIMENSIONS" = "768"
    "LLAMA_CPP_GPU_LAYERS" = ""
}

$legacyDefaults = @{
    "LLAMA_CPP_BASE_URL" = @("http://llama-chat:8080", "http://host.docker.internal:8080", "http://localhost:8080")
    "LLAMA_CPP_EMBED_BASE_URL" = @("http://llama-embed:8080")
    "LLAMA_CPP_CHAT_MODEL" = @("gemma3:1b", "gemma-3-1b-it", "google/gemma-3-1b-it", "unsloth/gemma-3-1b-it-GGUF")
    "LLAMA_CPP_CHAT_MODEL_FILE" = @("gemma-3-1b-it-Q4_K_M.gguf")
}

$lines = Get-Content -Path $envPath

foreach ($entry in $defaults.GetEnumerator()) {
    $key = $entry.Key
    $value = $entry.Value
    $pattern = "^" + [regex]::Escape($key) + "=(.*)$"
    $index = -1

    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match $pattern) {
            $index = $i
            break
        }
    }

    if ($index -eq -1) {
        $lines += "$key=$value"
        continue
    }

    $existing = ($lines[$index] -replace $pattern, '$1').Trim()
    $shouldReplaceLegacy = $legacyDefaults.ContainsKey($key) -and $legacyDefaults[$key] -contains $existing
    if ([string]::IsNullOrWhiteSpace($existing) -or $shouldReplaceLegacy) {
        $lines[$index] = "$key=$value"
    }
}

Set-Content -Path $envPath -Value $lines
Write-Info "Ensured local AI env defaults in src/aspire/.env"
