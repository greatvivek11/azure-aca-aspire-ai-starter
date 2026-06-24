#!/usr/bin/env bash
# Ensure src/aspire/.env exists and contains local AI defaults.
# Idempotent and non-destructive: existing custom non-empty values are preserved.
set -euo pipefail

info() { echo "[info] $*"; }

workspace_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_example_path="$workspace_root/src/aspire/.env.example"
env_path="$workspace_root/src/aspire/.env"

if [[ ! -f "$env_path" ]]; then
  if [[ ! -f "$env_example_path" ]]; then
    echo "[error] Missing src/aspire/.env.example. Cannot initialize .env file." >&2
    exit 1
  fi

  cp "$env_example_path" "$env_path"
  info "Created src/aspire/.env from .env.example"
fi

set_if_missing_or_empty() {
  local key="$1"
  local value="$2"

  if grep -q "^${key}=" "$env_path"; then
    local current
    current="$(grep "^${key}=" "$env_path" | head -n1 | cut -d'=' -f2-)"
    if [[ -z "${current//[[:space:]]/}" ]] || is_legacy_default "$key" "$current"; then
      sed -i.bak "s|^${key}=.*|${key}=${value}|" "$env_path"
      rm -f "$env_path.bak"
    fi
  else
    echo "${key}=${value}" >> "$env_path"
  fi
}

is_legacy_default() {
  local key="$1"
  local current="$2"

  case "$key:$current" in
    "LLAMA_CPP_BASE_URL:http://llama-chat:8080"|"LLAMA_CPP_BASE_URL:http://host.docker.internal:8080"|"LLAMA_CPP_BASE_URL:http://localhost:8080") return 0 ;;
    "LLAMA_CPP_EMBED_BASE_URL:http://llama-embed:8080") return 0 ;;
    "LLAMA_CPP_CHAT_MODEL:gemma3:1b"|"LLAMA_CPP_CHAT_MODEL:gemma-3-1b-it"|"LLAMA_CPP_CHAT_MODEL:google/gemma-3-1b-it"|"LLAMA_CPP_CHAT_MODEL:unsloth/gemma-3-1b-it-GGUF") return 0 ;;
    "LLAMA_CPP_CHAT_MODEL_FILE:gemma-3-1b-it-Q4_K_M.gguf") return 0 ;;
    *) return 1 ;;
  esac
}

set_if_missing_or_empty "AI_MODE" "local"
set_if_missing_or_empty "LLAMA_CPP_BASE_URL" "http://host.docker.internal:8082"
set_if_missing_or_empty "LLAMA_CPP_EMBED_BASE_URL" "http://host.docker.internal:8083"
set_if_missing_or_empty "LLAMA_CPP_MODELS_DIR" "$HOME/.local/share/llama.cpp/models"
set_if_missing_or_empty "LLAMA_CPP_BIN_DIR" "$HOME/.local/share/llama.cpp/bin"
set_if_missing_or_empty "LLAMA_CPP_CHAT_MODEL" "Qwen/Qwen2.5-0.5B-Instruct"
set_if_missing_or_empty "LLAMA_CPP_CHAT_MODEL_FILE" "Qwen2.5-0.5B-Instruct-Q4_K_M.gguf"
set_if_missing_or_empty "LLAMA_CPP_EMBED_MODEL" "nomic-embed-text"
set_if_missing_or_empty "LLAMA_CPP_EMBED_MODEL_FILE" "nomic-embed-text-v1.5.f16.gguf"
set_if_missing_or_empty "LLAMA_CPP_EMBED_DIMENSIONS" "768"
set_if_missing_or_empty "LLAMA_CPP_GPU_LAYERS" ""

info "Ensured local AI env defaults in src/aspire/.env"
