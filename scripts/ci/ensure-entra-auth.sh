#!/usr/bin/env bash
set -euo pipefail

mode="${1:-bootstrap}"
bootstrap_optional="$(echo "${ENTRA_AUTH_BOOTSTRAP_OPTIONAL:-false}" | tr '[:upper:]' '[:lower:]')"

set_entra_disabled_env() {
  if [[ "${SKIP_AZD_ENV_SET:-false}" != "true" ]]; then
    azd env set ENTRA_AUTH_ENABLED false >/dev/null
    azd env set ENTRA_TENANT_ID "" >/dev/null
    azd env set ENTRA_AUTHORITY "" >/dev/null
    azd env set ENTRA_API_CLIENT_ID "" >/dev/null
    azd env set ENTRA_AUDIENCE "" >/dev/null
    azd env set ENTRA_SPA_CLIENT_ID "" >/dev/null
    azd env set ENTRA_SCOPE "" >/dev/null
  fi

  if [[ -n "${GITHUB_ENV:-}" ]]; then
    {
      echo "ENTRA_AUTH_ENABLED=false"
      echo "ENTRA_TENANT_ID="
      echo "ENTRA_AUTHORITY="
      echo "ENTRA_API_CLIENT_ID="
      echo "ENTRA_AUDIENCE="
      echo "ENTRA_SPA_CLIENT_ID="
      echo "ENTRA_SCOPE="
    } >> "$GITHUB_ENV"
  fi
}

auth_enabled="$(echo "${ENTRA_AUTH_ENABLED:-true}" | tr '[:upper:]' '[:lower:]')"
if [[ "${auth_enabled}" != "true" ]]; then
  echo "Entra auth automation skipped because ENTRA_AUTH_ENABLED=${ENTRA_AUTH_ENABLED:-true}."
  set_entra_disabled_env
  exit 0
fi

if [[ -z "${AZURE_TENANT_ID:-}" ]]; then
  echo "AZURE_TENANT_ID is required for Entra auth automation."
  exit 1
fi

api_display_name="${AZURE_ENTRA_API_APP_NAME:-${AZD_ENVIRONMENT_NAME:-aca-aspire-ai}-api}"
spa_display_name="${AZURE_ENTRA_SPA_APP_NAME:-${AZD_ENVIRONMENT_NAME:-aca-aspire-ai}-spa}"
frontend_url="${FRONTEND_URL:-}"

api_app_id="${ENTRA_API_CLIENT_ID:-}"
spa_app_id="${ENTRA_SPA_CLIENT_ID:-}"

ensure_app() {
  local display_name="$1"
  local existing_app_id
  existing_app_id="$(az ad app list --display-name "$display_name" --query "[0].appId" -o tsv 2>/dev/null || true)"

  if [[ -n "$existing_app_id" ]]; then
    echo "$existing_app_id"
    return
  fi

  az ad app create \
    --display-name "$display_name" \
    --sign-in-audience "AzureADMyOrg" \
    --query appId -o tsv
}

ensure_sp() {
  local app_id="$1"
  az ad sp show --id "$app_id" >/dev/null 2>&1 || az ad sp create --id "$app_id" >/dev/null
}

get_app_object_id_with_retry() {
  local app_id="$1"
  local attempts="${2:-12}"
  local sleep_seconds="${3:-5}"
  local object_id=""

  for ((i=1; i<=attempts; i++)); do
    object_id="$(az ad app show --id "$app_id" --query id -o tsv 2>/dev/null || true)"
    if [[ -n "$object_id" ]]; then
      echo "$object_id"
      return
    fi

    if (( i < attempts )); then
      echo "Waiting for Entra app propagation (attempt ${i}/${attempts}) for appId=${app_id}..."
      sleep "$sleep_seconds"
    fi
  done

  echo "Failed to resolve Entra application object id for appId=${app_id} after ${attempts} attempts."
  exit 1
}

generate_guid() {
  if command -v uuidgen >/dev/null 2>&1; then
    uuidgen | tr '[:upper:]' '[:lower:]'
    return
  fi

  if command -v openssl >/dev/null 2>&1; then
    openssl rand -hex 16 | sed -E 's/(.{8})(.{4})(.{4})(.{4})(.{12})/\1-\2-\3-\4-\5/'
    return
  fi

  echo "Unable to generate GUID. Install 'uuidgen' (recommended) or 'openssl'." >&2
  exit 1
}

ensure_scope() {
  local app_id="$1"
  local object_id="$2"

  az ad app update --id "$app_id" --identifier-uris "api://${app_id}" >/dev/null

  local existing_scope_id
  existing_scope_id="$(az ad app show --id "$app_id" --query "api.oauth2PermissionScopes[?value=='access_as_user'] | [0].id" -o tsv 2>/dev/null || true)"
  if [[ "${existing_scope_id}" == "null" || "${existing_scope_id}" == "None" ]]; then
    existing_scope_id=""
  fi

  local scope_id="$existing_scope_id"
  if [[ -z "$scope_id" ]]; then
    scope_id="$(generate_guid)"
  fi

  if [[ -z "$scope_id" ]]; then
    echo "Failed to resolve OAuth scope id for app ${app_id}." >&2
    exit 1
  fi

  local payload_file
  payload_file="$(mktemp)"
  cat > "$payload_file" <<EOF
{
  "api": {
    "requestedAccessTokenVersion": 2,
    "oauth2PermissionScopes": [
      {
        "id": "${scope_id}",
        "adminConsentDescription": "Allow the app to access backend APIs on behalf of the signed in user.",
        "adminConsentDisplayName": "Access backend API",
        "isEnabled": true,
        "type": "User",
        "userConsentDescription": "Allow the application to access backend APIs on your behalf.",
        "userConsentDisplayName": "Access backend API",
        "value": "access_as_user"
      }
    ]
  }
}
EOF

  az rest \
    --method PATCH \
    --uri "https://graph.microsoft.com/v1.0/applications/${object_id}" \
    --headers "Content-Type=application/json" \
    --body @"$payload_file" >/dev/null

  rm -f "$payload_file"
  echo "$scope_id"
}

configure_spa_permissions() {
  local spa_object_id="$1"
  local target_api_app_id="$2"
  local scope_id="$3"

  if [[ -z "$scope_id" ]]; then
    echo "Scope id is empty; cannot configure SPA permissions." >&2
    exit 1
  fi

  local payload_file
  payload_file="$(mktemp)"
  cat > "$payload_file" <<EOF
{
  "requiredResourceAccess": [
    {
      "resourceAppId": "${target_api_app_id}",
      "resourceAccess": [
        {
          "id": "${scope_id}",
          "type": "Scope"
        }
      ]
    }
  ]
}
EOF

  az rest \
    --method PATCH \
    --uri "https://graph.microsoft.com/v1.0/applications/${spa_object_id}" \
    --headers "Content-Type=application/json" \
    --body @"$payload_file" >/dev/null

  rm -f "$payload_file"

  az ad app permission add \
    --id "$spa_app_id" \
    --api "$target_api_app_id" \
    --api-permissions "${scope_id}=Scope" >/dev/null 2>&1 || true

  if ! az ad app permission grant \
    --id "$spa_app_id" \
    --api "$target_api_app_id" \
    --scope "access_as_user" >/dev/null 2>&1; then
    echo "WARNING: 'az ad app permission grant' failed for SPA app ${spa_app_id}."
    echo "WARNING: Tenant policy may require manual approval."
  fi

  if ! az ad app permission admin-consent --id "$spa_app_id" >/dev/null 2>&1; then
    echo "WARNING: Admin consent did not succeed for SPA app ${spa_app_id}."
    echo "WARNING: Manual admin consent may be required for scope access_as_user."
  fi
}

configure_spa_redirects() {
  local app_id="$1"
  local urls=(
    "http://localhost:3000"
    "https://localhost:3000"
    "http://localhost:3000/auth-callback.html"
    "https://localhost:3000/auth-callback.html"
  )

  if [[ -n "$frontend_url" ]]; then
    # Normalize trailing slash and add both bare URL and /auth-callback.html variant
    frontend_url="${frontend_url%/}"
    urls+=("$frontend_url")
    urls+=("$frontend_url/auth-callback.html")
  fi

  # Newer Azure CLI supports --spa-redirect-uris directly.
  if az ad app update --id "$app_id" --spa-redirect-uris "${urls[@]}" >/dev/null 2>&1; then
    return
  fi

  # Fallback for older Azure CLI versions: patch Graph application.spa.redirectUris.
  local app_object_id
  app_object_id="$(get_app_object_id_with_retry "$app_id")"

  local redirect_uris_json="["
  for url in "${urls[@]}"; do
    redirect_uris_json+="$(printf '"%s",' "$url")"
  done
  redirect_uris_json="${redirect_uris_json%,}]"

  local payload_file
  payload_file="$(mktemp)"
  cat > "$payload_file" <<EOF
{
  "spa": {
    "redirectUris": ${redirect_uris_json}
  }
}
EOF

  az rest \
    --method PATCH \
    --uri "https://graph.microsoft.com/v1.0/applications/${app_object_id}" \
    --headers "Content-Type=application/json" \
    --body @"$payload_file" >/dev/null

  rm -f "$payload_file"
}

if [[ -z "$api_app_id" ]]; then
  api_app_id="$(ensure_app "$api_display_name")"
fi
if [[ -z "$spa_app_id" ]]; then
  spa_app_id="$(ensure_app "$spa_display_name")"
fi

run_bootstrap() {
  local api_object_id
  local spa_object_id
  local scope_id

  api_object_id="$(get_app_object_id_with_retry "$api_app_id")"
  spa_object_id="$(get_app_object_id_with_retry "$spa_app_id")"

  ensure_sp "$api_app_id"
  ensure_sp "$spa_app_id"

  scope_id="$(ensure_scope "$api_app_id" "$api_object_id")"
  configure_spa_permissions "$spa_object_id" "$api_app_id" "$scope_id"
  configure_spa_redirects "$spa_app_id"
}

if [[ "$mode" == "bootstrap" ]]; then
  bootstrap_output=""
  if ! bootstrap_output="$(run_bootstrap 2>&1)"; then
    echo "$bootstrap_output" >&2

    if [[ "$bootstrap_optional" == "true" ]] && echo "$bootstrap_output" | grep -Eqi "Insufficient privileges|Authorization_RequestDenied|does not have authorization"; then
      echo "WARNING: Skipping Entra bootstrap due to insufficient Graph privileges."
      echo "WARNING: Entra auth will be disabled for this CI run."
      set_entra_disabled_env
      exit 0
    fi

    echo "Entra bootstrap failed."
    exit 1
  fi

  if [[ -n "$bootstrap_output" ]]; then
    echo "$bootstrap_output"
  fi
elif [[ "$mode" == "finalize" ]]; then
  configure_spa_redirects "$spa_app_id"
else
  echo "Unsupported mode '$mode'. Use 'bootstrap' or 'finalize'."
  exit 1
fi

entra_authority="${ENTRA_AUTHORITY:-https://login.microsoftonline.com/${AZURE_TENANT_ID}/v2.0}"
entra_audience="${ENTRA_AUDIENCE:-api://${api_app_id}}"
entra_scope="${ENTRA_SCOPE:-api://${api_app_id}/access_as_user}"

if [[ "${SKIP_AZD_ENV_SET:-false}" != "true" ]]; then
  azd env set ENTRA_AUTH_ENABLED true >/dev/null
  azd env set ENTRA_TENANT_ID "$AZURE_TENANT_ID" >/dev/null
  azd env set ENTRA_AUTHORITY "$entra_authority" >/dev/null
  azd env set ENTRA_API_CLIENT_ID "$api_app_id" >/dev/null
  azd env set ENTRA_AUDIENCE "$entra_audience" >/dev/null
  azd env set ENTRA_SPA_CLIENT_ID "$spa_app_id" >/dev/null
  azd env set ENTRA_SCOPE "$entra_scope" >/dev/null
fi

if [[ -n "${GITHUB_ENV:-}" ]]; then
  {
    echo "ENTRA_AUTH_ENABLED=true"
    echo "ENTRA_TENANT_ID=${AZURE_TENANT_ID}"
    echo "ENTRA_AUTHORITY=${entra_authority}"
    echo "ENTRA_API_CLIENT_ID=${api_app_id}"
    echo "ENTRA_AUDIENCE=${entra_audience}"
    echo "ENTRA_SPA_CLIENT_ID=${spa_app_id}"
    echo "ENTRA_SCOPE=${entra_scope}"
  } >> "$GITHUB_ENV"
fi

echo "Entra auth automation complete. API app: ${api_app_id}, SPA app: ${spa_app_id}."
