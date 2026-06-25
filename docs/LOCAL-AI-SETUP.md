# Local AI Setup (Native llama.cpp with Devcontainer Support)

## Goal

A new user should be able to clone, open in VS Code (on host or devcontainer), wait for folder-open tasks, then press F5 and run local AI without manual setup steps.

## Architecture: Host vs Devcontainer

This project uses a **hybrid approach** to maximize developer experience across different environments:

### On Host Machine (Windows/Mac/Linux)
- **llama.cpp runs natively** as background processes on the host
- `llama-chat` on `127.0.0.1:8082`
- `llama-embed` on `127.0.0.1:8083`
- Backend/worker access via `http://host.docker.internal:8082|8083`
- **Benefit**: Native performance, GPU acceleration (if available), minimal resource overhead

### In Devcontainer
- **llama.cpp runs in Docker containers** orchestrated by Aspire
- Models cached in `~/.cache/llama.cpp/models` (persisted on host filesystem)
- `llama-chat` container on port 8082
- `llama-embed` container on port 8083
- Backend/worker access via internal container network: `http://llama-chat:8082|8083`
- **Benefit**: Cross-platform consistency, no host-native binary dependency, one-click setup

The setup script automatically detects the environment and configures accordingly:
- On host: installs native `llama-server` binary, starts servers
- In devcontainer: downloads models only, Aspire containers handle the rest

## What Runs Where

### Host Machine
- Host-native processes:
  - `llama-chat` on `127.0.0.1:8082`
  - `llama-embed` on `127.0.0.1:8083`
- Aspire Docker containers:
  - backend, worker, frontend
  - SQL, Redis, Azurite, Qdrant

Backend/worker call host-native llama.cpp with:
```env
LLAMA_CPP_BASE_URL=http://host.docker.internal:8082
LLAMA_CPP_EMBED_BASE_URL=http://host.docker.internal:8083
```

### Devcontainer
- Aspire-orchestrated containers:
  - backend, worker, frontend
  - SQL, Redis, Azurite, Qdrant
  - **llama-chat** (new, GPU disabled by default)
  - **llama-embed** (new, GPU disabled by default)
- Models cached in `~/.cache/llama.cpp/models` (mounted read-only)

Backend/worker call dockerized llama.cpp via internal network:
```env
LLAMA_CPP_BASE_URL=http://llama-chat:8082
LLAMA_CPP_EMBED_BASE_URL=http://llama-embed:8083
```

## Folder-Open Automation

On workspace open, VS Code tasks handle local setup:

1. ensure `src/aspire/.env` defaults
2. ensure Docker is running (for app infrastructure)
3. ensure llama.cpp (native on host, container in devcontainer)
4. verify chat + embedding endpoints are healthy

Because these run on folder open, users can wait for task completion and then press F5.

## Default Local AI Variables

```env
AI_MODE=local
LLAMA_CPP_BASE_URL=http://host.docker.internal:8082                    # (host) or http://llama-chat:8082 (devcontainer)
LLAMA_CPP_EMBED_BASE_URL=http://host.docker.internal:8083              # (host) or http://llama-embed:8083 (devcontainer)
LLAMA_CPP_MODELS_DIR=~/.local/share/llama.cpp/models                       # host-native cache
LLAMA_CPP_DEVCONTAINER_MODELS_DIR=~/.cache/llama.cpp/models                 # devcontainer cache
LLAMA_CPP_BIN_DIR=~/.local/share/llama.cpp/bin                          # (host only)
LLAMA_CPP_CHAT_MODEL=Qwen/Qwen2.5-0.5B-Instruct
LLAMA_CPP_CHAT_MODEL_FILE=Qwen2.5-0.5B-Instruct-Q4_K_M.gguf
LLAMA_CPP_EMBED_MODEL=nomic-embed-text
LLAMA_CPP_EMBED_MODEL_FILE=nomic-embed-text-v1.5.f16.gguf
LLAMA_CPP_GPU_LAYERS=                                                    # (set to >0 for GPU; host only, devcontainer defaults to CPU)
```

## Devcontainer-Specific Environment Variables

These override the default container images and model files:

```env
LLAMA_CPP_CHAT_IMAGE=ghcr.io/ggml-org/llama.cpp:server                 # Docker image for chat server
LLAMA_CPP_EMBED_IMAGE=ghcr.io/ggml-org/llama.cpp:server                # Docker image for embedding server
```

## Chat Model Fallbacks (<1GB)

The setup scripts try the primary chat model first:

- `Qwen/Qwen2.5-0.5B-Instruct` with `Qwen2.5-0.5B-Instruct-Q4_K_M.gguf` (~398MB)

If that download fails (for example transient 401/network issues), setup automatically falls back to:

- `unsloth/gemma-3-1b-it-GGUF` with `gemma-3-1b-it-Q4_K_M.gguf` (~806MB)
- `meta-llama/Llama-3.2-1B-Instruct` with `Llama-3.2-1B-Instruct-Q4_K_M.gguf` (~808MB)
- `Qwen/Qwen2.5-1.5B-Instruct` with `Qwen2.5-1.5B-Instruct-Q4_K_M.gguf` (~986MB)

When a fallback is selected, setup updates `LLAMA_CPP_CHAT_MODEL` and `LLAMA_CPP_CHAT_MODEL_FILE` in `src/aspire/.env` so future runs are stable.

### Hugging Face auth

If your network/account requires authentication for model downloads, set `HF_TOKEN` in your shell/session before running setup.

### About Phi/Mistral under 1GB

Requested Phi/Mistral options are not currently included in the automatic fallback list because commonly useful instruct GGUF quantizations are generally above 1GB.

## Validation

After folder-open tasks complete:

```bash
curl http://localhost:8082/health
curl http://localhost:8082/v1/models
curl http://localhost:8083/health
curl -X POST http://localhost:8083/v1/embeddings \
  -H "Content-Type: application/json" \
  -d '{"model":"nomic-embed-text","input":"Hello world"}'
```

```powershell
Invoke-RestMethod http://localhost:8082/health
Invoke-RestMethod http://localhost:8082/v1/models
Invoke-RestMethod http://localhost:8083/health

$Body = @{ model = "nomic-embed-text"; input = "Hello world" } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri http://localhost:8083/v1/embeddings -ContentType "application/json" -Body $Body
```

## Troubleshooting

### Setup task failed downloading llama.cpp

- Confirm internet access to:
  - `https://api.github.com/repos/ggml-org/llama.cpp/releases/latest`
  - Hugging Face model URLs
- If corporate restrictions block release downloads, set `LLAMA_CPP_SERVER_PATH` in the environment to a preinstalled `llama-server` path and rerun the setup task.

### Health endpoints are not up

- Check logs in `.vscode/llama-logs`.
- Confirm ports `8082` and `8083` are free.

### GPU acceleration

- Native mode can use platform-specific acceleration depending on the downloaded build and local drivers.
- `LLAMA_CPP_GPU_LAYERS` can be set (for example `35` or `999`) to request layer offload.
- Leave it empty for the safest cross-machine default.

## Azure Mode

Switch to Azure AI when needed:

```env
AI_MODE=azure
AZURE_OPENAI_API_KEY=your-key
AZURE_OPENAI_ENDPOINT=https://your-resource.openai.azure.com/
AZURE_OPENAI_MODEL_ID=gpt-5-mini
AZURE_SEARCH_ENDPOINT=https://your-search.search.windows.net/
AZURE_SEARCH_API_KEY=your-search-key
```
