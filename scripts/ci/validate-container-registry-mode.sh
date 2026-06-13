#!/usr/bin/env bash
set -euo pipefail

mode_lower="$(echo "${CONTAINER_REGISTRY_MODE:-external}" | tr '[:upper:]' '[:lower:]')"
echo "CONTAINER_REGISTRY_MODE=${mode_lower}" >> "$GITHUB_ENV"

if [[ "${mode_lower}" != "managed" && "${mode_lower}" != "external" ]]; then
  echo "CONTAINER_REGISTRY_MODE must be 'managed' or 'external'."
  exit 1
fi

if [[ "${mode_lower}" == "external" ]]; then
  if [[ -z "${EXTERNAL_REGISTRY_SERVER:-}" ]]; then
    echo "EXTERNAL_REGISTRY_SERVER is required when CONTAINER_REGISTRY_MODE=external."
    exit 1
  fi

  server_lower="$(echo "${EXTERNAL_REGISTRY_SERVER}" | tr '[:upper:]' '[:lower:]')"
  if [[ "${server_lower}" != "ghcr.io" && ( -z "${EXTERNAL_REGISTRY_USERNAME:-}" || -z "${EXTERNAL_REGISTRY_PASSWORD:-}" ) ]]; then
    echo "Authenticated external registries require EXTERNAL_REGISTRY_USERNAME and EXTERNAL_REGISTRY_PASSWORD."
    exit 1
  fi
fi
