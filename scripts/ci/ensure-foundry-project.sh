#!/usr/bin/env bash
set -euo pipefail

ai_mode="$(echo "${AI_SERVICES_PROVISIONING_MODE:-provision}" | tr '[:upper:]' '[:lower:]')"
project_mode="$(echo "${AZURE_AI_FOUNDRY_PROJECT_PROVISIONING_MODE:-cli}" | tr '[:upper:]' '[:lower:]')"

if [[ "${ai_mode}" != "provision" ]]; then
  echo "AI_SERVICES_PROVISIONING_MODE=${ai_mode}; skipping Foundry project ensure step."
  exit 0
fi

if [[ "${project_mode}" != "cli" ]]; then
  echo "Foundry project provisioning mode is '${project_mode}'; skipping CLI ensure step."
  exit 0
fi

rg="${DEPLOY_RESOURCE_GROUP}"
account_name="${AZURE_AI_SERVICES_ACCOUNT_NAME:-}"
if [[ -z "${account_name}" ]]; then
  account_name="$(azd env get-value AZURE_AI_SERVICES_ACCOUNT_NAME 2>/dev/null || true)"
fi
if [[ -z "${account_name}" ]]; then
  account_name="$(az cognitiveservices account list -g "${rg}" --query "[?kind=='AIServices'] | [0].name" -o tsv 2>/dev/null || true)"
fi

if [[ -z "${account_name}" ]]; then
  echo "Unable to resolve AIServices account name for Foundry project creation."
  exit 1
fi

project_name="${AZURE_AI_FOUNDRY_PROJECT_NAME:-enterprise-copilot}"
effective_location="${AZURE_LOCATION}"

echo "Foundry ensure debug: project_mode='${project_mode}', ai_mode='${ai_mode}', rg='${rg}', account='${account_name}', project='${project_name}', location='${effective_location}'"

if az cognitiveservices account project show -g "${rg}" -n "${account_name}" --project-name "${project_name}" -o none 2>/dev/null; then
  echo "Foundry project '${project_name}' already exists on '${account_name}'."
  exit 0
fi

echo "Creating Foundry project '${project_name}' on '${account_name}' using CLI mode..."
max_attempts=6
for attempt in $(seq 1 "${max_attempts}"); do
  create_log="$(mktemp)"
  set +e
  az cognitiveservices account project create \
    -g "${rg}" \
    -n "${account_name}" \
    --project-name "${project_name}" \
    -l "${effective_location}" \
    -o none 2>"${create_log}"
  create_exit_code=$?
  set -e

  if [[ "${create_exit_code}" -eq 0 ]]; then
    echo "Foundry project '${project_name}' created successfully."
    exit 0
  fi

  if grep -qi "must enable a managed identity" "${create_log}"; then
    echo "Attempt ${attempt}/${max_attempts}: identity propagation still in progress. Retrying in 30s..."
    sleep 30
    continue
  fi

  echo "Foundry project creation failed with a non-retryable error:"
  cat "${create_log}"
  exit "${create_exit_code}"
done

echo "Foundry project creation failed after ${max_attempts} retries waiting for identity propagation."
exit 1
