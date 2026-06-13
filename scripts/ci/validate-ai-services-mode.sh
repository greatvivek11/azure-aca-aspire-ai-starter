#!/usr/bin/env bash
set -euo pipefail

mode_lower="$(echo "${AI_SERVICES_PROVISIONING_MODE:-provision}" | tr '[:upper:]' '[:lower:]')"
if [[ "${mode_lower}" != "external" && "${mode_lower}" != "provision" ]]; then
  echo "AI_SERVICES_PROVISIONING_MODE must be 'external' or 'provision'."
  exit 1
fi

if [[ "${mode_lower}" == "external" ]]; then
  if [[ -z "${AZURE_OPENAI_ENDPOINT:-}" || -z "${AZURE_OPENAI_API_KEY:-}" || -z "${AZURE_OPENAI_MODEL_ID:-}" ]]; then
    echo "External AI mode requires AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, and AZURE_OPENAI_MODEL_ID secrets."
    exit 1
  fi
fi

echo "AI services mode validated: ${mode_lower}"
