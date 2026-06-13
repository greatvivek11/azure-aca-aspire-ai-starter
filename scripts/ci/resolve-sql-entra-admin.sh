#!/usr/bin/env bash
set -euo pipefail

sp_object_id="$(az ad sp show --id "${AZURE_CLIENT_ID}" --query id -o tsv)"
sp_display_name="$(az ad sp show --id "${AZURE_CLIENT_ID}" --query displayName -o tsv)"

if [[ -z "${sp_object_id}" || -z "${sp_display_name}" ]]; then
  echo "Unable to resolve service principal metadata for SQL Entra admin configuration."
  exit 1
fi

echo "AZURE_SQL_ENTRA_ADMIN_OBJECT_ID=${sp_object_id}" >> "$GITHUB_ENV"
echo "AZURE_SQL_ENTRA_ADMIN_LOGIN=${sp_display_name}" >> "$GITHUB_ENV"

echo "Resolved SQL Entra admin from OIDC principal: ${sp_display_name} (${sp_object_id})"
