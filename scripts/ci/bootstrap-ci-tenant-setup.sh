#!/usr/bin/env bash
set -euo pipefail

# One-time bootstrap for Azure tenant setup used by CI/CD deployment.
# Creates/reuses RG and CI SP, assigns Azure RBAC + Entra role, and can upsert OIDC federated credential.

DEFAULT_RESOURCE_GROUP="azure-aca-aspire-ai-starter-rg"
DEFAULT_LOCATION="southindia"
DEFAULT_SP_NAME="github-actions-copilot"
DEFAULT_GITHUB_ENVIRONMENT="dev"

subscription_id=""
resource_group="$DEFAULT_RESOURCE_GROUP"
location="$DEFAULT_LOCATION"
sp_name="$DEFAULT_SP_NAME"
azure_client_id=""
github_owner=""
github_repo=""
github_environment="$DEFAULT_GITHUB_ENVIRONMENT"

usage() {
  cat <<'EOF'
Usage:
  bash scripts/ci/bootstrap-ci-tenant-setup.sh --subscription-id <id> --github-owner <owner> --github-repo <repo> [options]

Required:
  --subscription-id <id>              Azure subscription id.
  --github-owner <owner>              GitHub owner/org for OIDC subject.
  --github-repo <repo>                GitHub repository name for OIDC subject.

Optional:
  --resource-group <name>             Default: azure-aca-aspire-ai-starter-rg
  --location <azure-region>           Default: southindia
  --sp-name <name>                    Default: github-actions-copilot
  --azure-client-id <appId>           Reuse an existing service principal app id.
  --github-environment <name>         Default: dev
  -h, --help                          Show this help.

Examples:
  bash scripts/ci/bootstrap-ci-tenant-setup.sh \
    --subscription-id "<sub-id>" \
    --github-owner "my-org" \
    --github-repo "azure-aca-aspire-ai-starter"

  bash scripts/ci/bootstrap-ci-tenant-setup.sh \
    --subscription-id "<sub-id>" \
    --resource-group "azure-aca-aspire-ai-starter-rg" \
    --location "southindia" \
    --github-owner "my-org" \
    --github-repo "azure-aca-aspire-ai-starter" \
    --github-environment "dev"
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --subscription-id)
      subscription_id="${2:-}"
      shift 2
      ;;
    --resource-group)
      resource_group="${2:-}"
      shift 2
      ;;
    --location)
      location="${2:-}"
      shift 2
      ;;
    --sp-name)
      sp_name="${2:-}"
      shift 2
      ;;
    --azure-client-id)
      azure_client_id="${2:-}"
      shift 2
      ;;
    --github-owner)
      github_owner="${2:-}"
      shift 2
      ;;
    --github-repo)
      github_repo="${2:-}"
      shift 2
      ;;
    --github-environment)
      github_environment="${2:-}"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 1
      ;;
  esac
done

if [[ -z "$subscription_id" ]]; then
  echo "--subscription-id is required." >&2
  usage
  exit 1
fi

if [[ -z "$github_owner" || -z "$github_repo" ]]; then
  echo "--github-owner and --github-repo are required." >&2
  usage
  exit 1
fi

if ! command -v az >/dev/null 2>&1; then
  echo "Azure CLI (az) is required." >&2
  exit 1
fi

if ! az account show >/dev/null 2>&1; then
  echo "Run 'az login' first." >&2
  exit 1
fi

az account set --subscription "$subscription_id"
tenant_id="$(az account show --query tenantId -o tsv)"

echo "[bootstrap] subscription: $subscription_id"
echo "[bootstrap] tenant: $tenant_id"
echo "[bootstrap] resource group: $resource_group"
echo "[bootstrap] location: $location"

# Ensure deployment resource group exists.
az group create \
  --subscription "$subscription_id" \
  --name "$resource_group" \
  --location "$location" >/dev/null

if [[ -z "$azure_client_id" ]]; then
  # Create or reuse CI service principal and capture app id.
  existing_app_id="$(az ad sp list --display-name "$sp_name" --query '[0].appId' -o tsv 2>/dev/null || true)"
  if [[ -n "$existing_app_id" ]]; then
    azure_client_id="$existing_app_id"
    echo "[bootstrap] reusing existing service principal appId: $azure_client_id"
  else
    azure_client_id="$(az ad sp create-for-rbac \
      --name "$sp_name" \
      --role Contributor \
      --scopes "/subscriptions/$subscription_id/resourceGroups/$resource_group" \
      --query appId -o tsv)"
    echo "[bootstrap] created service principal appId: $azure_client_id"
  fi
else
  echo "[bootstrap] using provided service principal appId: $azure_client_id"
fi

sp_object_id="$(az ad sp show --id "$azure_client_id" --query id -o tsv)"
scope="/subscriptions/$subscription_id/resourceGroups/$resource_group"

ensure_rbac_role() {
  local role_name="$1"
  local existing
  existing="$(az role assignment list \
    --assignee-object-id "$sp_object_id" \
    --scope "$scope" \
    --query "[?roleDefinitionName=='${role_name}'] | [0].id" -o tsv)"

  if [[ -n "$existing" ]]; then
    echo "[bootstrap] role already assigned: $role_name"
    return
  fi

  az role assignment create \
    --assignee-object-id "$sp_object_id" \
    --assignee-principal-type ServicePrincipal \
    --role "$role_name" \
    --scope "$scope" >/dev/null

  echo "[bootstrap] role assigned: $role_name"
}

# Ensure Azure RBAC used by infra/deploy.
ensure_rbac_role "Contributor"
ensure_rbac_role "User Access Administrator"

# Ensure tenant Entra role used by Entra app registration automation.
cloud_app_admin_role_id="$(az rest \
  --method GET \
  --uri "https://graph.microsoft.com/v1.0/roleManagement/directory/roleDefinitions?\$filter=displayName eq 'Cloud Application Administrator'" \
  --query 'value[0].id' -o tsv)"

if [[ -z "$cloud_app_admin_role_id" ]]; then
  echo "Failed to resolve Cloud Application Administrator role id." >&2
  exit 1
fi

dir_assignment_exists="$(az rest \
  --method GET \
  --uri "https://graph.microsoft.com/v1.0/roleManagement/directory/roleAssignments?\$filter=principalId eq '$sp_object_id' and roleDefinitionId eq '$cloud_app_admin_role_id'" \
  --query 'value[0].id' -o tsv 2>/dev/null || true)"

if [[ -n "$dir_assignment_exists" ]]; then
  echo "[bootstrap] Entra role already assigned: Cloud Application Administrator"
else
  az rest \
    --method POST \
    --uri "https://graph.microsoft.com/v1.0/roleManagement/directory/roleAssignments" \
    --headers "Content-Type=application/json" \
    --body "{\"principalId\":\"${sp_object_id}\",\"roleDefinitionId\":\"${cloud_app_admin_role_id}\",\"directoryScopeId\":\"/\"}" >/dev/null
  echo "[bootstrap] Entra role assigned: Cloud Application Administrator"
fi

# Upsert GitHub OIDC federated credential for environment-scoped workflow runs.
cred_name="github-actions-env-${github_environment}"
cred_file="$(mktemp)"

cat > "$cred_file" <<EOF
{
  "name": "${cred_name}",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:${github_owner}/${github_repo}:environment:${github_environment}",
  "description": "GitHub Actions environment ${github_environment}",
  "audiences": ["api://AzureADTokenExchange"]
}
EOF

existing_cred="$(az ad app federated-credential list --id "$azure_client_id" --query "[?name=='${cred_name}'].name" -o tsv)"
if [[ -n "$existing_cred" ]]; then
  az ad app federated-credential update \
    --id "$azure_client_id" \
    --federated-credential-id "$cred_name" \
    --parameters @"$cred_file" >/dev/null
  echo "[bootstrap] federated credential updated: $cred_name"
else
  az ad app federated-credential create \
    --id "$azure_client_id" \
    --parameters @"$cred_file" >/dev/null
  echo "[bootstrap] federated credential created: $cred_name"
fi

rm -f "$cred_file"

echo
echo "Bootstrap complete."
echo "Set these GitHub secrets:"
echo "  AZURE_SUBSCRIPTION_ID=$subscription_id"
echo "  AZURE_CLIENT_ID=$azure_client_id"
echo "  AZURE_TENANT_ID=$tenant_id"
