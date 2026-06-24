#!/usr/bin/env bash
# Verify native llama.cpp chat and embedding endpoints are healthy.
# Intended for pre-launch readiness checks after setup tasks start local servers.

set -euo pipefail

info() { printf '[local-llm-ready] %s\n' "$*"; }

workspace_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_path="$workspace_root/src/aspire/.env"

load_dotenv() {
  [[ -f "$env_path" ]] || return 0
  while IFS= read -r line || [[ -n "$line" ]]; do
    [[ -z "${line//[[:space:]]/}" || "${line#\#}" != "$line" ]] && continue
    key="${line%%=*}"
    value="${line#*=}"
    key="${key//[[:space:]]/}"
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

wait_endpoint() {
  local name="$1"
  local port="$2"
  local url="http://127.0.0.1:${port}/health"

  for _ in $(seq 1 90); do
    if curl -fsS --max-time 2 "$url" >/dev/null 2>&1; then
      info "$name is healthy at $url"
      return 0
    fi

    sleep 2
  done

  printf '[local-llm-ready] error: timed out waiting for %s at %s\n' "$name" "$url" >&2
  return 1
}

load_dotenv

chat_port="$(port_from_url "${LLAMA_CPP_BASE_URL:-http://host.docker.internal:8082}" 8082)"
embed_port="$(port_from_url "${LLAMA_CPP_EMBED_BASE_URL:-http://host.docker.internal:8083}" 8083)"

info "Waiting for native llama.cpp endpoints (chat:${chat_port}, embed:${embed_port})"
wait_endpoint "chat endpoint" "$chat_port"
wait_endpoint "embedding endpoint" "$embed_port"
info "Native llama.cpp readiness checks passed."
