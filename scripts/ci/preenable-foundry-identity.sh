#!/usr/bin/env bash
set -euo pipefail

ai_mode="$(echo "${AI_SERVICES_PROVISIONING_MODE:-provision}" | tr '[:upper:]' '[:lower:]')"
if [[ "${ai_mode}" != "provision" ]]; then
  echo "AI_SERVICES_PROVISIONING_MODE=${ai_mode}; skipping Foundry identity pre-check."
  exit 0
fi

rg="${DEPLOY_RESOURCE_GROUP}"

account_name="$(az cognitiveservices account list -g "${rg}" \
  --query "[?kind=='AIServices'] | [0].name" -o tsv 2>/dev/null || true)"

if [[ -z "${account_name}" ]]; then
  echo "No existing AIServices account found in '${rg}'. Identity will be configured during provision."
  exit 0
fi

identity_type="$(az cognitiveservices account show -g "${rg}" -n "${account_name}" \
  --query "identity.type" -o tsv 2>/dev/null || true)"

if [[ "${identity_type}" == "SystemAssigned" || "${identity_type}" == "SystemAssigned,UserAssigned" ]]; then
  echo "Account '${account_name}' already has managed identity (${identity_type}). No wait needed."
  exit 0
fi

echo "Enabling system-assigned managed identity on '${account_name}' before provision..."
az cognitiveservices account update \
  --resource-group "${rg}" \
  --name "${account_name}" \
  --assign-identity

echo "Waiting 45s for Foundry control plane to reflect the identity change..."
sleep 45

echo "Identity pre-enabled on '${account_name}'."
