#!/usr/bin/env bash
# Ensure llama.cpp and local GGUF model files are available.
# On host machines: installs native llama-server and starts servers.
# In devcontainers: downloads models and relies on Aspire-orchestrated containers.

set -euo pipefail

skip_model_download=false
validate_only=false
for arg in "$@"; do
  case "$arg" in
    --skip-model-download) skip_model_download=true ;;
    --validate-only) validate_only=true ;;
  esac
done

info() { printf '[local-llm] %s\n' "$*"; }
warn() { printf '[local-llm] warning: %s\n' "$*" >&2; }
err() { printf '[local-llm] error: %s\n' "$*" >&2; }

# Detect if running in devcontainer
is_in_devcontainer() {
  [[ "${DEVCONTAINERS:-}" == "true" ]] || [[ -f "/.dockerenv" ]]
}

workspace_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_path="$workspace_root/src/aspire/.env"

load_dotenv() {
  [[ -f "$env_path" ]] || return 0
  while IFS= read -r line || [[ -n "$line" ]]; do
    [[ -z "${line//[[:space:]]/}" || "${line#\#}" != "$line" ]] && continue
    key="${line%%=*}"
    value="${line#*=}"
    key="${key//[[:space:]]/}"
    value="${value%$'\r'}"
    value="${value#${value%%[![:space:]]*}}"
    value="${value%${value##*[![:space:]]}}"
    if [[ "$value" == \"*\" && "$value" == *\" ]]; then
      value="${value:1:-1}"
    elif [[ "$value" == \'*\' && "$value" == *\' ]]; then
      value="${value:1:-1}"
    fi
    [[ -z "$key" ]] && continue
    if [[ -z "${!key:-}" ]]; then
      export "$key=$value"
    fi
  done < "$env_path"
}

port_from_url() {
  local url="$1"
  local fallback="$2"
  local without_scheme="${url#*://}"
  local host_port="${without_scheme%%/*}"
  if [[ "$host_port" == *:* ]]; then
    printf '%s' "${host_port##*:}"
  else
    printf '%s' "$fallback"
  fi
}

load_dotenv

if is_in_devcontainer; then
  models_dir="${LLAMA_CPP_DEVCONTAINER_MODELS_DIR:-${HOME}/.cache/llama.cpp/models}"
else
  models_dir="${LLAMA_CPP_MODELS_DIR:-${HOME}/.local/share/llama.cpp/models}"
fi
bin_dir="${LLAMA_CPP_BIN_DIR:-${HOME}/.local/share/llama.cpp/bin}"
chat_base_url="${LLAMA_CPP_BASE_URL:-http://host.docker.internal:8082}"
embed_base_url="${LLAMA_CPP_EMBED_BASE_URL:-http://host.docker.internal:8083}"
chat_port="$(port_from_url "$chat_base_url" 8082)"
embed_port="$(port_from_url "$embed_base_url" 8083)"
chat_model="${LLAMA_CPP_CHAT_MODEL:-Qwen/Qwen2.5-0.5B-Instruct}"
embed_model="${LLAMA_CPP_EMBED_MODEL:-nomic-embed-text}"
chat_file="${LLAMA_CPP_CHAT_MODEL_FILE:-Qwen2.5-0.5B-Instruct-Q4_K_M.gguf}"
embed_file="${LLAMA_CPP_EMBED_MODEL_FILE:-nomic-embed-text-v1.5.f16.gguf}"
gpu_layers="${LLAMA_CPP_GPU_LAYERS:-}"
huggingface_token="${HF_TOKEN:-}"
log_dir="$workspace_root/.vscode/llama-logs"
selected_chat_file="$chat_file"
selected_chat_model="$chat_model"

model_file() {
  case "$1" in
    "$chat_model") printf '%s' "$chat_file" ;;
    "$embed_model") printf '%s' "$embed_file" ;;
    *) return 1 ;;
  esac
}

model_min_size_mb() {
  case "$1" in
    "$chat_model") printf '%s' '300' ;;
    "$embed_model") printf '%s' '200' ;;
    *) return 1 ;;
  esac
}

model_urls() {
  case "$1" in
    "$chat_model")
      printf '%s\n' \
        'https://huggingface.co/bartowski/Qwen2.5-0.5B-Instruct-GGUF/resolve/main/Qwen2.5-0.5B-Instruct-Q4_K_M.gguf?download=true' \
        'https://huggingface.co/bartowski/Qwen2.5-0.5B-Instruct-GGUF/resolve/main/Qwen2.5-0.5B-Instruct-Q4_K_M.gguf'
      ;;
    "$embed_model")
      printf '%s\n' \
        'https://huggingface.co/nomic-ai/nomic-embed-text-v1.5-GGUF/resolve/main/nomic-embed-text-v1.5.f16.gguf?download=true' \
        'https://huggingface.co/nomic-ai/nomic-embed-text-v1.5-GGUF/resolve/main/nomic-embed-text-v1.5.f16.gguf'
      ;;
    *) return 1 ;;
  esac
}

chat_fallback_specs() {
  cat <<'EOF'
unsloth/gemma-3-1b-it-GGUF|gemma-3-1b-it-Q4_K_M.gguf|600|https://huggingface.co/unsloth/gemma-3-1b-it-GGUF/resolve/main/gemma-3-1b-it-Q4_K_M.gguf?download=true|https://huggingface.co/unsloth/gemma-3-1b-it-GGUF/resolve/main/gemma-3-1b-it-Q4_K_M.gguf
meta-llama/Llama-3.2-1B-Instruct|Llama-3.2-1B-Instruct-Q4_K_M.gguf|600|https://huggingface.co/bartowski/Llama-3.2-1B-Instruct-GGUF/resolve/main/Llama-3.2-1B-Instruct-Q4_K_M.gguf?download=true|https://huggingface.co/bartowski/Llama-3.2-1B-Instruct-GGUF/resolve/main/Llama-3.2-1B-Instruct-Q4_K_M.gguf
Qwen/Qwen2.5-1.5B-Instruct|Qwen2.5-1.5B-Instruct-Q4_K_M.gguf|700|https://huggingface.co/bartowski/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/Qwen2.5-1.5B-Instruct-Q4_K_M.gguf?download=true|https://huggingface.co/bartowski/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/Qwen2.5-1.5B-Instruct-Q4_K_M.gguf
EOF
}

set_dotenv_value() {
  local key="$1"
  local value="$2"

  if [[ ! -f "$env_path" ]]; then
    printf '%s=%s\n' "$key" "$value" > "$env_path"
    return 0
  fi

  local tmp
  tmp="$(mktemp)"
  awk -v k="$key" -v v="$value" '
    BEGIN { updated=0 }
    {
      if ($0 ~ "^[[:space:]]*" k "[[:space:]]*=") {
        print k "=" v
        updated=1
      } else {
        print $0
      }
    }
    END {
      if (!updated) {
        print k "=" v
      }
    }
  ' "$env_path" > "$tmp"
  mv "$tmp" "$env_path"
}

persist_resolved_chat_model() {
  if [[ "$selected_chat_model" == "$chat_model" && "$selected_chat_file" == "$chat_file" ]]; then
    return 0
  fi

  set_dotenv_value "LLAMA_CPP_CHAT_MODEL" "$selected_chat_model"
  set_dotenv_value "LLAMA_CPP_CHAT_MODEL_FILE" "$selected_chat_file"
  warn "Persisted fallback chat model to .env: $selected_chat_model ($selected_chat_file)"
}

file_size_mb() {
  local path="$1"
  local size_bytes
  if stat --version >/dev/null 2>&1; then
    size_bytes="$(stat -c%s -- "$path" 2>/dev/null || true)"
  else
    size_bytes="$(stat -f%z -- "$path" 2>/dev/null || true)"
  fi

  [[ "$size_bytes" =~ ^[0-9]+$ ]] || return 1
  awk -v bytes="$size_bytes" 'BEGIN { printf "%.1f", bytes / 1048576 }'
}

model_file_meets_min_size() {
  local model="$1"
  local path="$2"
  local min_size_mb="$3"
  local delete_if_small="$4"

  [[ -f "$path" ]] || return 1

  local size_mb
  if ! size_mb="$(file_size_mb "$path")"; then
    warn "Could not determine file size for $model: $path"
    if [[ "$delete_if_small" == true ]]; then
      rm -f "$path"
      warn "Removed unreadable model file: $path"
    fi
    return 1
  fi

  if awk -v actual="$size_mb" -v min="$min_size_mb" 'BEGIN { exit !(actual >= min) }'; then
    info "Model available: $model (${size_mb} MB)"
    return 0
  fi

  warn "Model file appears incomplete for $model (${size_mb} MB < expected minimum ${min_size_mb} MB): $path"
  if [[ "$delete_if_small" == true ]]; then
    rm -f "$path"
    warn "Removed incomplete model file: $path"
  fi

  return 1
}

download_with_fallbacks() {
  local model="$1"
  local destination="$2"
  local min_size_mb="$3"
  shift 2
  shift 1
  local last_error=""
  local auth_args=()

  if [[ -n "$huggingface_token" ]]; then
    auth_args=(-H "Authorization: Bearer $huggingface_token")
  fi

  if [[ "$#" -gt 0 ]]; then
    for url in "$@"; do
      [[ -z "$url" ]] && continue
      info "Attempting download for $model from $url"

      if command -v curl >/dev/null 2>&1; then
        if curl -fL --retry 6 --retry-all-errors -A "azure-aca-aspire-ai-starter-local-setup/1.0" -C - "${auth_args[@]}" -o "$destination" "$url"; then
          if model_file_meets_min_size "$model" "$destination" "$min_size_mb" false; then
            return 0
          fi
          last_error="downloaded file smaller than expected minimum ${min_size_mb}MB"
          rm -f "$destination"
          continue
        fi
        last_error="curl failed for $url"
      elif command -v wget >/dev/null 2>&1; then
        if wget -c --user-agent="azure-aca-aspire-ai-starter-local-setup/1.0" -O "$destination" "$url"; then
          if model_file_meets_min_size "$model" "$destination" "$min_size_mb" false; then
            return 0
          fi
          last_error="downloaded file smaller than expected minimum ${min_size_mb}MB"
          rm -f "$destination"
          continue
        fi
        last_error="wget failed for $url"
      else
        err "curl or wget is required to download models"
        return 1
      fi
    done
  else
    while IFS= read -r url; do
      [[ -z "$url" ]] && continue
      info "Attempting download for $model from $url"

      if command -v curl >/dev/null 2>&1; then
        if curl -fL --retry 6 --retry-all-errors -A "azure-aca-aspire-ai-starter-local-setup/1.0" -C - "${auth_args[@]}" -o "$destination" "$url"; then
          if model_file_meets_min_size "$model" "$destination" "$min_size_mb" false; then
            return 0
          fi
          last_error="downloaded file smaller than expected minimum ${min_size_mb}MB"
          rm -f "$destination"
          continue
        fi
        last_error="curl failed for $url"
      elif command -v wget >/dev/null 2>&1; then
        if wget -c --user-agent="azure-aca-aspire-ai-starter-local-setup/1.0" -O "$destination" "$url"; then
          if model_file_meets_min_size "$model" "$destination" "$min_size_mb" false; then
            return 0
          fi
          last_error="downloaded file smaller than expected minimum ${min_size_mb}MB"
          rm -f "$destination"
          continue
        fi
        last_error="wget failed for $url"
      else
        err "curl or wget is required to download models"
        return 1
      fi
    done < <(model_urls "$model")
  fi

  if [[ "$last_error" == *"401"* ]]; then
    warn "Hugging Face returned 401 for $model. If auth is required on your network, set HF_TOKEN and rerun setup."
  fi

  err "Failed to download $model from all candidate URLs: $last_error"
  return 1
}

ensure_chat_model() {
  if model_available "$chat_model"; then
    selected_chat_file="$chat_file"
    selected_chat_model="$chat_model"
    info "Model available: $chat_model"
    return 0
  fi

  if [[ "$skip_model_download" != true ]]; then
    mkdir -p "$models_dir"
    info "Downloading $chat_model to $models_dir/$chat_file"
    if download_with_fallbacks "$chat_model" "$models_dir/$chat_file" "$(model_min_size_mb "$chat_model")"; then
      selected_chat_file="$chat_file"
      selected_chat_model="$chat_model"
      return 0
    fi
  fi

  warn "Primary chat model '$chat_model' unavailable. Trying fallback chat models under 1GB..."
  while IFS='|' read -r fallback_name fallback_file fallback_min_size_mb fallback_url1 fallback_url2; do
    [[ -z "$fallback_file" ]] && continue

    if model_file_meets_min_size "$fallback_name" "$models_dir/$fallback_file" "$fallback_min_size_mb" true; then
      warn "Using fallback chat model file: $fallback_file"
      selected_chat_file="$fallback_file"
      selected_chat_model="$fallback_name"
      return 0
    fi

    if [[ "$skip_model_download" == true ]]; then
      continue
    fi

    mkdir -p "$models_dir"
    info "Downloading fallback $fallback_name to $models_dir/$fallback_file"
    if download_with_fallbacks "$fallback_name" "$models_dir/$fallback_file" "$fallback_min_size_mb" "$fallback_url1" "$fallback_url2"; then
      warn "Downloaded fallback chat model: $fallback_name"
      selected_chat_file="$fallback_file"
      selected_chat_model="$fallback_name"
      return 0
    fi
  done < <(chat_fallback_specs)

  err "No usable chat model could be downloaded."
  return 1
}

model_available() {
  local model="$1"
  local file
  file="$(model_file "$model")"
  model_file_meets_min_size "$model" "$models_dir/$file" "$(model_min_size_mb "$model")" true
}

download_model() {
  local model="$1"
  local file
  file="$(model_file "$model")"

  if model_available "$model"; then
    info "Model available: $model"
    return 0
  fi

  if [[ "$skip_model_download" == true ]]; then
    warn "Model download skipped; missing $model at $models_dir/$file"
    return 1
  fi

  mkdir -p "$models_dir"
  info "Downloading $model to $models_dir/$file"
  download_with_fallbacks "$model" "$models_dir/$file" "$(model_min_size_mb "$model")"
}

find_llama_server() {
  if [[ -n "${LLAMA_CPP_SERVER_PATH:-}" && -x "${LLAMA_CPP_SERVER_PATH}" ]]; then
    printf '%s' "$LLAMA_CPP_SERVER_PATH"
    return 0
  fi

  [[ -d "$bin_dir" ]] || return 1
  find "$bin_dir" -type f \( -name 'llama-server' -o -name 'llama-server.exe' \) 2>/dev/null | head -n1
}

select_asset_regex() {
  case "$(uname -s):$(uname -m)" in
    Darwin:arm64) printf 'bin-macos-arm64\.tar\.gz$' ;;
    Darwin:*) printf 'bin-macos-x64\.tar\.gz$' ;;
    Linux:x86_64) printf 'bin-ubuntu-vulkan-x64\.tar\.gz$|bin-ubuntu-x64\.tar\.gz$' ;;
    Linux:aarch64|Linux:arm64) printf 'bin-ubuntu-arm64\.tar\.gz$|bin-ubuntu-vulkan-arm64\.tar\.gz$' ;;
    *) return 1 ;;
  esac
}

install_llama_cpp() {
  local existing
  existing="$(find_llama_server || true)"
  if [[ -n "$existing" ]]; then
    info "llama-server available: $existing"
    printf '%s' "$existing"
    return 0
  fi

  local regex asset_url archive install_root
  regex="$(select_asset_regex)" || {
    err "Unsupported OS/architecture for automatic llama.cpp download. Set LLAMA_CPP_SERVER_PATH."
    return 1
  }

  command -v tar >/dev/null 2>&1 || {
    err "tar is required to install llama.cpp automatically"
    return 1
  }

  mkdir -p "$bin_dir"
  info "Downloading native llama.cpp release"
  asset_url="$(curl -fsSL https://api.github.com/repos/ggml-org/llama.cpp/releases/latest \
    | grep 'browser_download_url' \
    | cut -d '"' -f 4 \
    | grep -E "$regex" \
    | head -n1)"

  if [[ -z "$asset_url" ]]; then
    err "Could not find a matching llama.cpp release asset. Set LLAMA_CPP_SERVER_PATH."
    return 1
  fi

  archive="$bin_dir/$(basename "$asset_url")"
  install_root="$bin_dir/native"
  curl -fL --retry 3 -o "$archive" "$asset_url"
  rm -rf "$install_root"
  mkdir -p "$install_root"
  tar -xzf "$archive" -C "$install_root"
  find "$install_root" -type f -name 'llama-server' -exec chmod +x {} \;

  existing="$(find_llama_server || true)"
  if [[ -z "$existing" ]]; then
    err "Downloaded llama.cpp release did not contain llama-server. Set LLAMA_CPP_SERVER_PATH."
    return 1
  fi

  info "Installed llama-server: $existing"
  printf '%s' "$existing"
}

server_ready() {
  local port="$1"
  curl -fsS --max-time 2 "http://127.0.0.1:${port}/health" >/dev/null 2>&1
}

start_server() {
  local name="$1"
  local port="$2"
  local file="$3"
  local alias="$4"
  local embedding="$5"
  local server_path="$6"
  local model_path="$models_dir/$file"

  if server_ready "$port"; then
    info "$name already responding on port $port"
    return 0
  fi

  if [[ ! -f "$model_path" ]]; then
    err "Missing model file for $name: $model_path"
    return 1
  fi

  mkdir -p "$log_dir"
  local args=(--host 0.0.0.0 --port "$port" --model "$model_path" --alias "$alias")
  [[ "$embedding" == true ]] && args+=(--embedding)
  [[ -n "$gpu_layers" ]] && args+=(--n-gpu-layers "$gpu_layers")

  info "Starting native $name on port $port"
  nohup "$server_path" "${args[@]}" >"$log_dir/$name.out.log" 2>"$log_dir/$name.err.log" &
  echo $! >"$log_dir/$name.pid"

  for _ in $(seq 1 90); do
    if server_ready "$port"; then
      info "$name is ready on port $port"
      return 0
    fi
    sleep 2
  done

  err "$name did not become ready within 3 minutes. Check $log_dir/$name.err.log"
  return 1
}

info "Ensuring llama.cpp setup"
info "Model cache: $models_dir"
if is_in_devcontainer; then
  info "Running in devcontainer; llama.cpp will be managed by Aspire containers"
else
  info "Binary cache: $bin_dir"
fi

if [[ "$validate_only" == true ]]; then
  model_available "$chat_model" && info "Model available: $chat_model" || true
  model_available "$embed_model" && info "Model available: $embed_model" || true
  if ! is_in_devcontainer; then
    find_llama_server >/dev/null 2>&1 && info "llama-server available" || true
  fi
  info "Validation complete; downloads and server startup skipped."
  exit 0
fi

ensure_chat_model
download_model "$embed_model"

if is_in_devcontainer; then
  info "Configuring for devcontainer-based llama.cpp"
  set_dotenv_value "LLAMA_CPP_DEVCONTAINER_MODELS_DIR" "$models_dir"
  info "Models downloaded to $models_dir"
  info "Aspire will start llama-chat and llama-embed containers on startup"
  exit 0
else
  # On host machine: setup native binaries and start servers
  server_path="$(install_llama_cpp)"
  persist_resolved_chat_model
  start_server "llama-chat" "$chat_port" "$selected_chat_file" "$selected_chat_model" false "$server_path"
  start_server "llama-embed" "$embed_port" "$embed_file" "$embed_model" true "$server_path"
  info "Native llama.cpp chat and embedding servers are ready."
fi
