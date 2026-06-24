# Local AI Setup (Native llama.cpp)

## Goal

A new user should be able to clone, open in VS Code, wait for folder-open tasks, then press F5 and run local AI without manual setup steps.

## What Runs Where

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

## Folder-Open Automation

On workspace open, VS Code tasks handle local setup:

1. ensure `src/aspire/.env` defaults
2. ensure Docker is running (for app infrastructure)
3. ensure native llama.cpp (`llama-server`)
4. verify native chat + embedding endpoints are healthy

Because these run on folder open, users can wait for task completion and then press F5.

## Default Local AI Variables

```env
AI_MODE=local
LLAMA_CPP_BASE_URL=http://host.docker.internal:8082
LLAMA_CPP_EMBED_BASE_URL=http://host.docker.internal:8083
LLAMA_CPP_MODELS_DIR=
LLAMA_CPP_BIN_DIR=
LLAMA_CPP_CHAT_MODEL=Qwen/Qwen2.5-0.5B-Instruct
LLAMA_CPP_CHAT_MODEL_FILE=Qwen2.5-0.5B-Instruct-Q4_K_M.gguf
LLAMA_CPP_EMBED_MODEL=nomic-embed-text
LLAMA_CPP_EMBED_MODEL_FILE=nomic-embed-text-v1.5.f16.gguf
LLAMA_CPP_GPU_LAYERS=
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
