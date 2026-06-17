#!/usr/bin/env bash
set -euo pipefail

# One-command local Entra auth bootstrap for Aspire dev.
# - Creates/reuses API + SPA app registrations
# - Configures API scope and SPA permissions
# - Writes ENTRA_* values into src/aspire/.env
#
# Prerequisites:
# - az CLI logged in (az login)
# - permissions to create/update app registrations in tenant

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ASPIRE_DIR="${ROOT_DIR}/src/aspire"
ENV_FILE="${ASPIRE_DIR}/.env"
ENV_EXAMPLE_FILE="${ASPIRE_DIR}/.env.example"

if ! command -v az >/dev/null 2>&1; then
  echo "Azure CLI (az) is required. Install and run 'az login' first."
  exit 1
fi

if ! az account show >/dev/null 2>&1; then
  echo "No active Azure login found. Run: az login"
  exit 1
fi

mkdir -p "${ASPIRE_DIR}"
if [[ ! -f "${ENV_FILE}" && -f "${ENV_EXAMPLE_FILE}" ]]; then
  cp "${ENV_EXAMPLE_FILE}" "${ENV_FILE}"
  echo "Created ${ENV_FILE} from .env.example"
fi

tenant_id="${AZURE_TENANT_ID:-$(az account show --query tenantId -o tsv)}"
if [[ -z "${tenant_id}" ]]; then
  echo "Unable to resolve tenant id. Set AZURE_TENANT_ID or ensure az account is configured."
  exit 1
fi

# Keep names stable and tenant-scoped to avoid creating new app registrations every run.
api_name="${AZURE_ENTRA_API_APP_NAME:-aca-aspire-ai-local-api}"
spa_name="${AZURE_ENTRA_SPA_APP_NAME:-aca-aspire-ai-local-spa}"

# Reuse CI script logic, but avoid azd env writes and capture emitted values.
temp_env="$(mktemp)"
cleanup() {
  rm -f "${temp_env}"
}
trap cleanup EXIT

export ENTRA_AUTH_ENABLED=true
export AZURE_TENANT_ID="${tenant_id}"
export AZURE_ENTRA_API_APP_NAME="${api_name}"
export AZURE_ENTRA_SPA_APP_NAME="${spa_name}"
export SKIP_AZD_ENV_SET=true
export GITHUB_ENV="${temp_env}"

bash "${ROOT_DIR}/scripts/ci/ensure-entra-auth.sh" bootstrap

# shellcheck disable=SC1090
source "${temp_env}"

if [[ -z "${ENTRA_API_CLIENT_ID:-}" || -z "${ENTRA_SPA_CLIENT_ID:-}" ]]; then
  echo "Failed to bootstrap Entra app registrations."
  exit 1
fi

upsert_env_var() {
  local key="$1"
  local value="$2"

  if [[ ! -f "${ENV_FILE}" ]]; then
    printf '%s=%s\n' "$key" "$value" >> "${ENV_FILE}"
    return
  fi

  if grep -Eq "^${key}=" "${ENV_FILE}"; then
    sed -i.bak -E "s|^${key}=.*$|${key}=${value}|" "${ENV_FILE}"
  else
    printf '%s=%s\n' "$key" "$value" >> "${ENV_FILE}"
  fi
}

upsert_env_var "ENTRA_AUTH_ENABLED" "true"
upsert_env_var "ENTRA_TENANT_ID" "${ENTRA_TENANT_ID}"
upsert_env_var "ENTRA_AUTHORITY" "${ENTRA_AUTHORITY}"
upsert_env_var "ENTRA_API_CLIENT_ID" "${ENTRA_API_CLIENT_ID}"
upsert_env_var "ENTRA_AUDIENCE" "${ENTRA_AUDIENCE}"
upsert_env_var "ENTRA_SPA_CLIENT_ID" "${ENTRA_SPA_CLIENT_ID}"
upsert_env_var "ENTRA_SCOPE" "${ENTRA_SCOPE}"

rm -f "${ENV_FILE}.bak"

echo ""
echo "Local Entra auth bootstrap complete."
echo "Updated ${ENV_FILE} with ENTRA_* values."
echo "Now restart Aspire (F5) and use Sign in in the frontend."
