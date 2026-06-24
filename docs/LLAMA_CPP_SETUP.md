# llama.cpp Local AI Setup (Native Host Mode)

This repository uses native host llama.cpp for local AI mode.

## First-Clone Flow

On workspace open, VS Code folder-open tasks prepare local AI before launch:

1. Ensure src/aspire/.env defaults are present.
2. Ensure Docker is available for application containers.
3. Ensure native llama.cpp binaries are available on the host.
4. Download chat and embedding GGUF models into local cache when missing.
5. Start two host-native llama-server processes:
   - chat: http://127.0.0.1:8082
   - embeddings: http://127.0.0.1:8083

Backend and worker containers reach these host-native services via Docker Desktop DNS:

LLAMA_CPP_BASE_URL=http://host.docker.internal:8082
LLAMA_CPP_EMBED_BASE_URL=http://host.docker.internal:8083

## Why Two Servers

llama.cpp serves one model per server process. Chat and embeddings use different GGUF models, so two processes are started.

## Key Configuration

Local AI values in src/aspire/.env:

- AI_MODE=local
- LLAMA_CPP_BASE_URL
- LLAMA_CPP_EMBED_BASE_URL
- LLAMA_CPP_MODELS_DIR
- LLAMA_CPP_BIN_DIR
- LLAMA_CPP_CHAT_MODEL
- LLAMA_CPP_CHAT_MODEL_FILE
- LLAMA_CPP_EMBED_MODEL
- LLAMA_CPP_EMBED_MODEL_FILE
- LLAMA_CPP_GPU_LAYERS

If LLAMA_CPP_MODELS_DIR or LLAMA_CPP_BIN_DIR is empty, user-local defaults are used.

## Chat Model Fallback Order

If the preferred chat model cannot be downloaded, setup tries this order:

1. Qwen/Qwen2.5-0.5B-Instruct
2. unsloth/gemma-3-1b-it-GGUF
3. meta-llama/Llama-3.2-1B-Instruct
4. Qwen/Qwen2.5-1.5B-Instruct

When a fallback is selected, setup persists LLAMA_CPP_CHAT_MODEL and LLAMA_CPP_CHAT_MODEL_FILE in src/aspire/.env.

## Quick Validation

- curl http://localhost:8082/health
- curl http://localhost:8083/health
- curl http://localhost:8082/v1/models

For end-to-end local workflow and troubleshooting, see docs/LOCAL-AI-SETUP.md.