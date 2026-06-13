#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${ACTIONS_ID_TOKEN_REQUEST_URL:-}" || -z "${ACTIONS_ID_TOKEN_REQUEST_TOKEN:-}" ]]; then
  echo "OIDC token request variables are missing. Ensure workflow permission id-token: write is enabled."
  exit 1
fi

federated_token="$(curl -sS -H "Authorization: bearer ${ACTIONS_ID_TOKEN_REQUEST_TOKEN}" "${ACTIONS_ID_TOKEN_REQUEST_URL}&audience=api://AzureADTokenExchange" | jq -r '.value')"

if [[ -z "${federated_token}" || "${federated_token}" == "null" ]]; then
  echo "Failed to obtain federated OIDC token for Azure login."
  exit 1
fi

az login --service-principal \
  --username "${AZURE_CLIENT_ID}" \
  --tenant "${AZURE_TENANT_ID}" \
  --federated-token "${federated_token}" \
  --allow-no-subscriptions

az account set --subscription "${AZURE_SUBSCRIPTION_ID}"
