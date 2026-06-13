#!/usr/bin/env bash
set -euo pipefail

server_lower="$(echo "${EXTERNAL_REGISTRY_SERVER}" | tr '[:upper:]' '[:lower:]')"
owner_lower="$(echo "${GITHUB_REPOSITORY_OWNER}" | tr '[:upper:]' '[:lower:]')"
repo_lower="$(basename "${GITHUB_REPOSITORY}" | tr '[:upper:]' '[:lower:]')"

{
  echo "EXTERNAL_REGISTRY_SERVER=${server_lower}"
  echo "BACKEND_IMAGE=${server_lower}/${owner_lower}/${repo_lower}-backend:${GITHUB_SHA}"
  echo "FRONTEND_IMAGE=${server_lower}/${owner_lower}/${repo_lower}-frontend:${GITHUB_SHA}"
  echo "WORKER_IMAGE=${server_lower}/${owner_lower}/${repo_lower}-worker:${GITHUB_SHA}"
} >> "$GITHUB_ENV"
