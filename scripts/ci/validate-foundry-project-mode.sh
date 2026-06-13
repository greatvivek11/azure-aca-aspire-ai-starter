#!/usr/bin/env bash
set -euo pipefail

mode_lower="$(echo "${AZURE_AI_FOUNDRY_PROJECT_PROVISIONING_MODE:-cli}" | tr '[:upper:]' '[:lower:]')"
if [[ "${mode_lower}" != "arm" && "${mode_lower}" != "cli" ]]; then
  echo "AZURE_AI_FOUNDRY_PROJECT_PROVISIONING_MODE must be 'arm' or 'cli'."
  exit 1
fi

if [[ "${mode_lower}" == "arm" ]]; then
  echo "ARM mode can fail due to Foundry identity propagation timing. CLI mode is recommended."
fi

echo "Foundry project provisioning mode validated: ${mode_lower}"
