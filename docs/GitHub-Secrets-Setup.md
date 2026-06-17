# GitHub Secrets Setup & Configuration

This guide explains how to configure GitHub Secrets for CI/CD deployment using GitHub Actions.

> Source-of-truth note:
> Required/optional secret handling is ultimately defined by `.github/workflows/reusable-deploy-azure.yml` and `scripts/ci/*.sh` validation scripts.
> If this document diverges from workflow validation behavior, follow workflow + scripts.

## Why GitHub Secrets?

Currently, we store sensitive credentials in GitHub Secrets instead of Azure Key Vault for these reasons:

1. **Simplicity**: Reduced overhead for PoC phase; no Key Vault provisioning needed
2. **Cost**: Avoids additional Azure resource costs during early development
3. **Convenience**: Direct injection into Container Apps environment variables
4. **Safety**: GitHub Secrets are encrypted and only exposed to Actions workflows

**Future**: As the project grows to production, migrate to Azure Key Vault with Managed Identities for enhanced security and audit logging.

---

## Quick Bootstrap Checklist (5-10 min)

Use this if you want the shortest path to first successful deployment.

1. Run one-time tenant bootstrap script (required: subscription id, GitHub owner, GitHub repo; optional: resource group and location).
2. Create GitHub Environment `dev` (UI or `gh api`).
3. Verify Entra federated credential subject matches your GitHub environment.
4. Add required repository secrets:
  - `AZURE_SUBSCRIPTION_ID`, `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`
  - `AZURE_SQL_ADMIN_LOGIN`, `AZURE_SQL_ADMIN_PASSWORD`
  - `ENTRA_AUTH_ENABLED=true` (recommended)
  - `AI_SERVICES_PROVISIONING_MODE` (optional, defaults to `provision`)
  - `AZURE_OPENAI_API_KEY`, `AZURE_OPENAI_MODEL_ID`, `AZURE_OPENAI_ENDPOINT` (only when `AI_SERVICES_PROVISIONING_MODE=external`)
  - Optional cost controls: `CONTAINER_REGISTRY_MODE=external`, `ENABLE_LOG_ANALYTICS=false`, replica counts `0`
5. Trigger workflow:
  - Actions -> Deploy to Azure Container Apps -> Run workflow -> `environment=dev`
6. If OIDC fails, verify federated credential subject exactly matches environment.

Jump links:
- SP creation: [Step 1](#1-create-service-principal-for-github-actions)
- GitHub Environment: [Step 2](#2-create-github-environment-required-for-environment-based-oidc)
- OIDC federated credential: [Step 3](#3-configure-oidc-trust-recommended)

---

## Required GitHub Secrets

### Azure Authentication (Required for Deployment)

These secrets enable GitHub Actions to authenticate to Azure using OpenID Connect (OIDC).

| Secret Name | Description | Example |
|---|---|---|
| `AZURE_SUBSCRIPTION_ID` | Your Azure subscription ID | `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` |
| `AZURE_CLIENT_ID` | Service Principal client ID | `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` |
| `AZURE_TENANT_ID` | Azure Entra ID tenant ID | `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` |

### AI Services Credentials (Required only for external AI mode)

These secrets are injected into the Container Apps environment at deployment time.

| Secret Name | Description | Source |
|---|---|---|
| `AZURE_OPENAI_API_KEY` | Azure OpenAI API key | Azure Portal → OpenAI resource → Keys and Endpoint |
| `AZURE_OPENAI_MODEL_ID` | Deployed model name | Azure Portal → OpenAI resource → Model deployments |
| `AZURE_OPENAI_ENDPOINT` | Azure OpenAI endpoint URL | Azure Portal → OpenAI resource → Keys and Endpoint |

These three secrets are required only when `AI_SERVICES_PROVISIONING_MODE=external`.

### AI Services Provisioning (Optional, for zero-manual bootstrap)

Use these to control infra behavior. Default behavior provisions a new Azure AI Foundry resource + project + model deployments.

| Secret Name | Description | Default |
|---|---|---|
| `AI_SERVICES_PROVISIONING_MODE` | `external` (reuse existing) or `provision` (create new Foundry infra) | `provision` |
| `AZURE_AI_SERVICES_ACCOUNT_NAME` | Optional Azure AI Foundry resource name override | empty (auto-generated) |
| `AZURE_AI_SERVICES_RESTORE` | Restore soft-deleted Azure AI account when name collision occurs (`true`/`false`) | `false` (auto-enabled during CI retry on restore-required errors) |
| `AZURE_OPENAI_CHAT_MODEL_NAME` | Chat model catalog name for deployment | `gpt-chat-latest` |
| `AZURE_OPENAI_CHAT_MODEL_VERSION` | Chat model version (provider/region dependent) | empty (provider chooses compatible default) |
| `AZURE_OPENAI_EMBEDDING_MODEL_ID` | Embeddings deployment name | `text-embedding-3-small` |
| `AZURE_OPENAI_EMBEDDING_MODEL_NAME` | Embeddings model catalog name | `text-embedding-3-small` |
| `AZURE_OPENAI_EMBEDDING_MODEL_VERSION` | Embeddings model version | `1` |

### SQL Credentials (Required for Provisioning only)

These secrets are used by Bicep during `azd provision` to create Azure SQL Server/Database.
Backend runtime SQL auth now uses the Container App User-Assigned Managed Identity (UAMI),
so SQL username/password is not injected into ACA runtime secrets.

| Secret Name | Description | Example |
|---|---|---|
| `AZURE_SQL_ADMIN_LOGIN` | Azure SQL server admin username | `sqladmincopilot` |
| `AZURE_SQL_ADMIN_PASSWORD` | Azure SQL server admin password | `Use-a-strong-password-here` |

### SQL Entra AD Authentication (Automated in provision mode)

No Entra admin secrets are required for new environment provisioning.

In GitHub Actions provision mode (`SQL_PROVISIONING_MODE=provision`), the workflow automatically:
1. Resolves the OIDC service principal metadata (`AZURE_CLIENT_ID`) and configures it as SQL Entra admin.
2. Creates a contained database user for backend UAMI.
3. Grants `db_datareader`, `db_datawriter`, and `db_ddladmin` roles to the UAMI.

For existing SQL mode (`SQL_PROVISIONING_MODE=existing`), automatic role grants are skipped. In that mode,
you may need to run the SQL user/role grant commands manually if the existing database is not already configured.

### SQL Provisioning Mode (Optional, Recommended for Free-Tier Reuse)

Use these secrets when you want CI/CD to use a pre-created SQL database (for example, your free-lifetime offer) instead of provisioning a new one.

| Secret Name | Description | Default |
|---|---|---|
| `SQL_PROVISIONING_MODE` | `provision` or `existing` | `provision` |
| `AZURE_SQL_EXISTING_SERVER_NAME` | Existing SQL server name (without `.database.windows.net`) | empty |
| `AZURE_SQL_EXISTING_DATABASE_NAME` | Existing SQL database name | empty |

Behavior:
- `provision`: Bicep creates SQL Server + DB and backend receives `SQL_SERVER` + `SQL_DATABASE` env vars.
- `existing`: Bicep skips SQL creation and backend uses your existing server/database values.

When `SQL_PROVISIONING_MODE=existing`, both existing-name secrets are required.

### Deployment Configuration (Optional)

| Secret Name | Description | Default |
|---|---|---|
| `AZD_ENVIRONMENT_NAME` | Azure Developer CLI environment name | `azure-aca-aspire-ai-starter` |
| `CONTAINER_REGISTRY_MODE` | `external` for public/authenticated external images, `managed` to provision ACR and use `azd deploy` | `external` |
| `EXTERNAL_REGISTRY_SERVER` | Registry hostname used in external mode | `ghcr.io` |
| `EXTERNAL_REGISTRY_USERNAME` | Required for authenticated non-GHCR registries | empty |
| `EXTERNAL_REGISTRY_PASSWORD` | Required for authenticated non-GHCR registries | empty |
| `ENABLE_LOG_ANALYTICS` | Enable Log Analytics + workspace-based App Insights | `false` |
| `ENABLE_ASPIRE_DASHBOARD` | Enable ACA Aspire Dashboard component | `true` |
| `BACKEND_MIN_REPLICAS` | Backend minimum replicas | `0` |
| `FRONTEND_MIN_REPLICAS` | Frontend minimum replicas | `0` |
| `WORKER_MIN_REPLICAS` | Worker minimum replicas | `0` |
| `AZURE_STORAGE_ACCOUNT_NAME` | Optional storage account name override | empty (auto-generated) |
| `AZURE_STORAGE_DOCUMENTS_CONTAINER` | Blob container for document uploads | `documents` |
| `AZURE_SEARCH_SERVICE_NAME` | Optional AI Search service name override | empty (auto-generated) |
| `AZURE_SEARCH_INDEX_NAME` | AI Search index name for chunks | `documents-index` |
| `ENTRA_AUTH_ENABLED` | Enable Entra auth wiring for frontend/backend | `true` |
| `ENTRA_AUTHORITY` | Optional Entra authority override | auto-generated from tenant |
| `ENTRA_API_CLIENT_ID` | Optional existing API app client ID to reuse | empty (workflow auto-creates) |
| `ENTRA_AUDIENCE` | Optional backend token audience override | `api://<api-client-id>` |
| `ENTRA_SPA_CLIENT_ID` | Optional existing SPA app client ID to reuse | empty (workflow auto-creates) |
| `ENTRA_SCOPE` | Optional delegated scope override for SPA | `api://<api-client-id>/access_as_user` |
| `AZURE_ENTRA_API_APP_NAME` | Optional display name for auto-created API app | `<azd-env>-api` |
| `AZURE_ENTRA_SPA_APP_NAME` | Optional display name for auto-created SPA app | `<azd-env>-spa` |

### Entra App Registration Automation

The deploy workflow now automates Entra app setup when `ENTRA_AUTH_ENABLED=true`:

1. Creates or reuses API and SPA app registrations.
2. Exposes API scope `access_as_user` on the API app.
3. Adds SPA delegated permission to that API scope.
4. Attempts delegated permission grant/admin consent (tenant policies may still require manual approval).
5. Wires `ENTRA_*` values into azd environment variables used by Bicep.
6. Updates SPA redirect URIs with localhost defaults and deployed frontend URL.

Default behavior:
- `CONTAINER_REGISTRY_MODE=external` builds and pushes **public GHCR** images in GitHub Actions, then provisions ACA with those images.
- `CONTAINER_REGISTRY_MODE=managed` provisions Azure Container Registry and keeps the older `azd deploy` source-build path.
- `ENABLE_ASPIRE_DASHBOARD=true` creates the Aspire Dashboard component in the ACA environment on first deploy.
- Replica defaults are `0`, so ACA can scale all three apps down to zero when idle.
- `ENABLE_LOG_ANALYTICS=false` avoids always-on Log Analytics workspace charges by default. When Aspire Dashboard is enabled, ACA uses `azure-monitor` app logs destination instead of `none` to satisfy platform validation.
- Container app names are fixed for PoC readability: `backend`, `frontend`, `worker`.

Important GHCR note:
- The workflow attempts to set GHCR packages to public. If package visibility remains private, ACA pull will fail unless `EXTERNAL_REGISTRY_USERNAME` and `EXTERNAL_REGISTRY_PASSWORD` are set.

---

## Step-by-Step Setup

### 1. Create Service Principal for GitHub Actions

```bash
bash scripts/ci/bootstrap-ci-tenant-setup.sh \
  --subscription-id "<your-subscription-id>" \
  --github-owner "<owner>" \
  --github-repo "<repo>"
```

Required parameter:
- `--subscription-id`
- `--github-owner`
- `--github-repo`

Optional parameters (defaults):
- `--resource-group` (default: `azure-aca-aspire-ai-starter-rg`)
- `--location` (default: `southindia`)
- `--sp-name` (default: `github-actions-copilot`)
- `--azure-client-id` (reuse existing service principal instead of creating one)

Optional OIDC flag:
- `--github-environment` (default: `dev`)

What the script does in one run:
- Creates/reuses resource group.
- Creates/reuses CI service principal.
- Assigns Azure RBAC on RG: `Contributor`, `User Access Administrator`.
- Assigns tenant Entra directory role: `Cloud Application Administrator`.
- Creates/updates federated credential for GitHub environment subject.

Output includes values to copy into GitHub secrets:
- `AZURE_SUBSCRIPTION_ID`
- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`

Example with explicit optional values and OIDC upsert:

```bash
bash scripts/ci/bootstrap-ci-tenant-setup.sh \
  --subscription-id "<your-subscription-id>" \
  --resource-group "azure-aca-aspire-ai-starter-rg" \
  --location "southindia" \
  --github-owner "<owner>" \
  --github-repo "<repo>" \
  --github-environment "dev"
```

### 2. Create GitHub Environment (Required for environment-based OIDC)

The deployment workflow uses a GitHub Environment (default: `dev`) and expects
an OIDC subject in the format:

`repo:<owner>/<repo>:environment:<environment-name>`

Create it in GitHub UI:
- Repository -> Settings -> Environments -> New environment -> `dev`

Or with GitHub CLI:

```bash
gh api \
  --method PUT \
  -H "Accept: application/vnd.github+json" \
  /repos/<owner>/<repo>/environments/dev
```

Notes:
- Repository secrets continue to work by default.
- Environment secrets/variables are optional overrides if you want per-environment values.

### 3. Configure OIDC Trust (Recommended)

Instead of storing credentials, use OIDC for secure, keyless authentication:

```bash
GITHUB_OWNER="<your-github-owner>"
GITHUB_REPO="<your-github-repo>"
GITHUB_ENVIRONMENT="dev"
AZURE_CLIENT_ID="<appId-from-step-1>"

CREDENTIAL_NAME="github-actions-env-${GITHUB_ENVIRONMENT}"

cat > federated-credential.json <<EOF
{
  "name": "${CREDENTIAL_NAME}",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:${GITHUB_OWNER}/${GITHUB_REPO}:environment:${GITHUB_ENVIRONMENT}",
  "description": "GitHub Actions environment ${GITHUB_ENVIRONMENT}",
  "audiences": [
    "api://AzureADTokenExchange"
  ]
}
EOF

# Upsert: update if already exists, create otherwise
EXISTING=$(az ad app federated-credential list --id "$AZURE_CLIENT_ID" \
  --query "[?name=='${CREDENTIAL_NAME}'].name" -o tsv)

if [[ -n "$EXISTING" ]]; then
  echo "Updating existing federated credential '${CREDENTIAL_NAME}'..."
  az ad app federated-credential update \
    --id "$AZURE_CLIENT_ID" \
    --federated-credential-id "$CREDENTIAL_NAME" \
    --parameters @federated-credential.json
else
  echo "Creating federated credential '${CREDENTIAL_NAME}'..."
  az ad app federated-credential create \
    --id "$AZURE_CLIENT_ID" \
    --parameters @federated-credential.json
fi
```

Verify the credential was created/updated correctly:

```bash
az ad app federated-credential list \
  --id "$AZURE_CLIENT_ID" \
  --query "[].{name:name, subject:subject, issuer:issuer}" \
  -o table
```

Expected output shows `subject` as `repo:<owner>/<repo>:environment:<environment>`. If the subject still shows an old repo name, re-run the upsert block above.

If you deploy to multiple environments, create one federated credential per environment
subject (for example: `dev`, `staging`, `prod`).

Why this is a manual bootstrap step:
- The workflow cannot create its first federated credential for itself.
- Azure login requires the federated credential to already exist.
- After first bootstrap, further automation is possible.

If you intentionally use a User Assigned Managed Identity instead of a service principal, use this command instead (note: identity name must be the managed identity resource name, not appId/clientId):

```bash
az identity federated-credential create \
  --name "github-actions-env-${GITHUB_ENVIRONMENT}" \
  --identity-name "<managed-identity-resource-name>" \
  --resource-group "<managed-identity-resource-group>" \
  --issuer "https://token.actions.githubusercontent.com" \
  --subject "repo:${GITHUB_OWNER}/${GITHUB_REPO}:environment:${GITHUB_ENVIRONMENT}" \
  --audiences "api://AzureADTokenExchange"
```

### 4. Add Secrets to GitHub Repository

Go to: **Settings → Secrets and variables → Actions → New repository secret**

#### Azure Authentication Secrets:
```
AZURE_SUBSCRIPTION_ID: <your-subscription-id>
AZURE_CLIENT_ID: <appId-from-service-principal>
AZURE_TENANT_ID: <tenant-from-service-principal>
```

#### AI Services Secrets:
1. Navigate to Azure Portal → OpenAI resource
2. Go to **Keys and Endpoint**
3. Copy:
   - **Key 1** → `AZURE_OPENAI_API_KEY`
   - **Endpoint** → `AZURE_OPENAI_ENDPOINT`
4. Go to **Model deployments** → copy deployed model name → `AZURE_OPENAI_MODEL_ID`

```
AZURE_OPENAI_API_KEY: <your-openai-api-key>
AZURE_OPENAI_MODEL_ID: <deployed-model-name> (e.g., gpt-5-mini or gpt-5-nano)
AZURE_OPENAI_ENDPOINT: <your-openai-endpoint>
```

#### SQL Provisioning Secrets (provision-time only):
```
AZURE_SQL_ADMIN_LOGIN: <azure-sql-admin-username>
AZURE_SQL_ADMIN_PASSWORD: <azure-sql-admin-password>
```

#### Optional Configuration:
```
AZD_ENVIRONMENT_NAME: azure-aca-aspire-ai-starter
CONTAINER_REGISTRY_MODE: external
EXTERNAL_REGISTRY_SERVER: ghcr.io
ENABLE_LOG_ANALYTICS: false
ENABLE_ASPIRE_DASHBOARD: true
BACKEND_MIN_REPLICAS: 0
FRONTEND_MIN_REPLICAS: 0
WORKER_MIN_REPLICAS: 0
SQL_PROVISIONING_MODE: provision
# Required only when SQL_PROVISIONING_MODE=existing
AZURE_SQL_EXISTING_SERVER_NAME: <existing-sql-server-name>
AZURE_SQL_EXISTING_DATABASE_NAME: <existing-database-name>
```

#### Entra Admin Secrets:
Not required for provision mode. Leave unset unless you intentionally override the automated behavior.

---

## Verification

### Verify Secrets Are Configured:

```bash
gh secret list --repo <owner>/<repo>
```

### Test GitHub Actions Workflow:

```bash
# Trigger deployment manually via GitHub UI:
# Actions → Deploy to Azure Container Apps → Run workflow → main branch
```

Monitor the workflow at: **Actions → Deploy to Azure Container Apps → Latest run**

---

## Security Best Practices

1. ✅ **Use OIDC**: Eliminates the need to store credentials in GitHub
2. ✅ **Rotate Keys Regularly**: Update `AZURE_OPENAI_API_KEY` every 90 days
3. ✅ **Scope Permissions**: Service Principal should be resource-group scoped and include only required roles (`Contributor` + `User Access Administrator`)
4. ✅ **Audit Access**: Review "Secret Audit Log" in GitHub Settings
5. ✅ **Never Commit Secrets**: Ensure `.env` files are in `.gitignore`
6. ⚠️ **Future**: Migrate to Azure Key Vault with Managed Identities for production

---

## Troubleshooting

### ❌ "Insufficient permissions" error in GitHub Actions

**Cause**: Service Principal lacks required permissions  
**Solution**:
- Re-run the one-time bootstrap script in Step 1; it assigns required Azure RBAC and Entra directory role.
- If failure is from `scripts/ci/ensure-entra-auth.sh bootstrap` with `Insufficient privileges to complete the operation`, verify the CI principal has Entra `Cloud Application Administrator` at tenant scope.

Azure RBAC commands:
```bash
az role assignment create \
  --assignee <appId> \
  --role Contributor \
  --scope /subscriptions/<SUBSCRIPTION_ID>/resourceGroups/<RESOURCE_GROUP>

az role assignment create \
  --assignee <appId> \
  --role "User Access Administrator" \
  --scope /subscriptions/<SUBSCRIPTION_ID>/resourceGroups/<RESOURCE_GROUP>
```

### ❌ "Azure OpenAI credentials not found" during deployment

**Cause**: Secrets not configured in GitHub  
**Solution**: Verify all AI Services secrets are set (see Step 4 above)

### ❌ OIDC "Subject not recognized" error

**Cause**: Federated credential configuration mismatch  
**Solution**: Verify issuer and subject match:
```bash
az ad app federated-credential list \
  --id <AZURE_CLIENT_ID>
```

Expected `subject` must match the workflow token exactly:
- For this repository workflow, default is `repo:<owner>/<repo>:environment:dev`
- For manual deploys to staging/prod, create matching subjects for `staging` and `prod`

### ❌ "ParentResourceNotFound" for userAssignedIdentities/federatedIdentityCredentials

**Cause**: Using managed-identity command with a service principal appId/clientId, or wrong managed identity name/resource group  
**Solution**: If your workflow uses `AZURE_CLIENT_ID` from a service principal/app registration, use `az ad app federated-credential create` (Step 2). Only use `az identity federated-credential create` when you have an existing User Assigned Managed Identity.

---

## Environment Variable Injection

When GitHub Actions runs `azd provision` (and `azd deploy` only in managed ACR mode), environment values are passed to the infra template and workloads:

```yaml
env:
  AZURE_OPENAI_API_KEY: ${{ secrets.AZURE_OPENAI_API_KEY }}
  AZURE_OPENAI_MODEL_ID: ${{ secrets.AZURE_OPENAI_MODEL_ID }}
  AZURE_OPENAI_ENDPOINT: ${{ secrets.AZURE_OPENAI_ENDPOINT }}
```

These become Container Apps environment variables and are accessible at runtime.
For SQL, backend now uses Managed Identity with Bicep-provided `SQL_SERVER`, `SQL_DATABASE`, and `AZURE_CLIENT_ID`.

---

## Next Steps

1. ✅ Configure GitHub Secrets (this guide)
2. ⏭️ Commit code and push to `main`
3. ⏭️ GitHub Actions workflow will trigger automatically
4. ⏭️ Monitor deployment in **Actions → Deploy to Azure Container Apps**
