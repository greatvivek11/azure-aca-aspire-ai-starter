#!/usr/bin/env bash
# Ensure Docker engine is running on folder-open.
# Idempotent: safe to run repeatedly.
set -euo pipefail

info() { echo "[info] $*"; }
warn() { echo "[warn] $*" >&2; }

if ! command -v docker >/dev/null 2>&1; then
  warn "Docker CLI is not installed. Install Docker Desktop/Engine to run local containers."
  exit 1
fi

if docker info >/dev/null 2>&1; then
  info "Docker engine is already running."
  exit 0
fi

case "$(uname)" in
  Darwin)
    if [[ -d "/Applications/Docker.app" ]]; then
      info "Starting Docker Desktop..."
      open -a Docker
    else
      warn "Docker Desktop app not found in /Applications."
      exit 1
    fi
    ;;
  Linux)
    if command -v systemctl >/dev/null 2>&1; then
      info "Attempting to start Docker service..."
      systemctl --user start docker 2>/dev/null || sudo systemctl start docker 2>/dev/null || true
    fi
    ;;
  *)
    warn "Unsupported OS for ensure-docker.sh"
    exit 1
    ;;
esac

for _ in $(seq 1 90); do
  if docker info >/dev/null 2>&1; then
    info "Docker engine is ready."
    exit 0
  fi
  sleep 2
done

warn "Docker engine did not become ready within 3 minutes."
exit 1
