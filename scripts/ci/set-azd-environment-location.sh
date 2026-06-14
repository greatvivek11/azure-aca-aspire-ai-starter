#!/usr/bin/env bash
set -euo pipefail

requested_location="${AZURE_LOCATION:-southindia}"
resource_group_name="${AZD_ENVIRONMENT_NAME}-rg"
existing_rg_location="$(az group show --name "${resource_group_name}" --query location -o tsv 2>/dev/null || true)"

if [[ -n "${existing_rg_location}" ]]; then
  effective_location="${existing_rg_location}"
  echo "Resource group '${resource_group_name}' already exists in '${effective_location}'. Reusing that location."
else
  effective_location="${requested_location}"
  echo "Resource group '${resource_group_name}' does not exist. Using requested location '${effective_location}'."
fi

if azd env select "${AZD_ENVIRONMENT_NAME}" >/dev/null 2>&1; then
  echo "Selected existing azd environment '${AZD_ENVIRONMENT_NAME}'."
else
  azd env new "${AZD_ENVIRONMENT_NAME}" --location "${effective_location}" --no-prompt
fi

azd env set AZURE_LOCATION "${effective_location}"
azd env set AZURE_RESOURCE_GROUP "${resource_group_name}"

{
  echo "AZURE_LOCATION=${effective_location}"
  echo "DEPLOY_RESOURCE_GROUP=${resource_group_name}"
} >> "$GITHUB_ENV"

echo "Using Azure location: ${effective_location}"
