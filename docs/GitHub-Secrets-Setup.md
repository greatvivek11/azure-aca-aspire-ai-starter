# GitHub Secrets Setup & Configuration

This guide explains how to configure GitHub Secrets for CI/CD deployment using GitHub Actions.

## Why GitHub Secrets?

Currently, we store sensitive credentials in GitHub Secrets instead of Azure Key Vault for these reasons:

1. **Simplicity**: Reduced overhead for PoC phase; no Key Vault provisioning needed
2. **Cost**: Avoids additional Azure resource costs during early development
3. **Convenience**: Direct injection into Container Apps environment variables
4. **Safety**: GitHub Secrets are encrypted and only exposed to Actions workflows

**Future**: As the project grows to production, migrate to Azure Key Vault with Managed Identities for enhanced security and audit logging.

---

## Required GitHub Secrets

### Azure Authentication (Required for Deployment)

These secrets enable GitHub Actions to authenticate to Azure using OpenID Connect (OIDC).

| Secret Name | Description | Example |
|---|---|---|
| `AZURE_SUBSCRIPTION_ID` | Your Azure subscription ID | `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` |
| `AZURE_CLIENT_ID` | Service Principal client ID | `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` |
| `AZURE_TENANT_ID` | Azure Entra ID tenant ID | `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` |

### AI Services Credentials (Required for Runtime)

These secrets are injected into the Container Apps environment at deployment time.

| Secret Name | Description | Source |
|---|---|---|
| `AZURE_OPENAI_API_KEY` | Azure OpenAI API key | Azure Portal → OpenAI resource → Keys and Endpoint |
| `AZURE_OPENAI_MODEL_ID` | Deployed model name | Azure Portal → OpenAI resource → Model deployments |
| `AZURE_OPENAI_ENDPOINT` | Azure OpenAI endpoint URL | Azure Portal → OpenAI resource → Keys and Endpoint |

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
| `AZD_ENVIRONMENT_NAME` | Azure Developer CLI environment name | `copilot-sk-azure` |

---

## Step-by-Step Setup

### 1. Create Service Principal for GitHub Actions

```bash
# Create a service principal
az ad sp create-for-rbac \
  --name "github-actions-copilot" \
  --role Contributor \
  --scope /subscriptions/<YOUR_SUBSCRIPTION_ID>
```

**Output** will contain:
- `appId` → Set as `AZURE_CLIENT_ID`
- `tenant` → Set as `AZURE_TENANT_ID`

### 1.1 Required RBAC for CI Service Principal

This repository's Bicep template creates `Microsoft.Authorization/roleAssignments`
for Container Apps managed identities (AcrPull on ACR). Because of that, the CI
service principal used by `azure/login` must have role-assignment write
permissions at the deployment scope.

Minimum required roles on the target resource group (recommended):
- `Contributor`
- `User Access Administrator`

Broader alternative:
- `Owner`

Example (resource-group scope):

```bash
SUBSCRIPTION_ID="<subscription-id>"
RESOURCE_GROUP="<resource-group>"
APP_ID="<AZURE_CLIENT_ID>"

SP_OBJECT_ID=$(az ad sp show --id "$APP_ID" --query id -o tsv)

az role assignment create \
  --assignee-object-id "$SP_OBJECT_ID" \
  --assignee-principal-type ServicePrincipal \
  --role "Contributor" \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP"

az role assignment create \
  --assignee-object-id "$SP_OBJECT_ID" \
  --assignee-principal-type ServicePrincipal \
  --role "User Access Administrator" \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP"
```

Verify:

```bash
az role assignment list \
  --assignee-object-id "$SP_OBJECT_ID" \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP" \
  -o table
```

### 2. Configure OIDC Trust (Recommended)

Instead of storing credentials, use OIDC for secure, keyless authentication:

```bash
GITHUB_ORG="<your-github-org>"
GITHUB_REPO="<your-github-repo>"
AZURE_CLIENT_ID="<appId-from-step-1>"

# Add federated credentials to the Entra application (service principal)
cat > federated-credential.json <<EOF
{
  "name": "github-actions-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:${GITHUB_ORG}/${GITHUB_REPO}:ref:refs/heads/main",
  "description": "GitHub Actions main branch",
  "audiences": [
    "api://AzureADTokenExchange"
  ]
}
EOF

az ad app federated-credential create \
  --id "$AZURE_CLIENT_ID" \
  --parameters @federated-credential.json
```

If you intentionally use a User Assigned Managed Identity instead of a service principal, use this command instead (note: identity name must be the managed identity resource name, not appId/clientId):

```bash
az identity federated-credential create \
  --name "github-actions-main" \
  --identity-name "<managed-identity-resource-name>" \
  --resource-group "<managed-identity-resource-group>" \
  --issuer "https://token.actions.githubusercontent.com" \
  --subject "repo:${GITHUB_ORG}/${GITHUB_REPO}:ref:refs/heads/main" \
  --audiences "api://AzureADTokenExchange"
```

### 3. Add Secrets to GitHub Repository

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
AZURE_OPENAI_MODEL_ID: <deployed-model-name> (e.g., gpt-4.1)
AZURE_OPENAI_ENDPOINT: <your-openai-endpoint>
```

#### SQL Provisioning Secrets (provision-time only):
```
AZURE_SQL_ADMIN_LOGIN: <azure-sql-admin-username>
AZURE_SQL_ADMIN_PASSWORD: <azure-sql-admin-password>
```

#### Optional Configuration:
```
AZD_ENVIRONMENT_NAME: copilot-sk-azure
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
3. ✅ **Scope Permissions**: Service Principal should have only `Contributor` role on the resource group, not subscription-wide
4. ✅ **Audit Access**: Review "Secret Audit Log" in GitHub Settings
5. ✅ **Never Commit Secrets**: Ensure `.env` files are in `.gitignore`
6. ⚠️ **Future**: Migrate to Azure Key Vault with Managed Identities for production

---

## Troubleshooting

### ❌ "Insufficient permissions" error in GitHub Actions

**Cause**: Service Principal lacks required permissions  
**Solution**: Grant `Contributor` role on the resource group:
```bash
az role assignment create \
  --assignee <appId> \
  --role Contributor \
  --scope /subscriptions/<SUBSCRIPTION_ID>/resourceGroups/<RESOURCE_GROUP>
```

### ❌ "Azure OpenAI credentials not found" during deployment

**Cause**: Secrets not configured in GitHub  
**Solution**: Verify all AI Services secrets are set (see Step 3 above)

### ❌ OIDC "Subject not recognized" error

**Cause**: Federated credential configuration mismatch  
**Solution**: Verify issuer and subject match:
```bash
az ad app federated-credential list \
  --id <AZURE_CLIENT_ID>
```

Expected `subject` must match the workflow token exactly:
- If workflow job does not set `environment`, use `repo:<org>/<repo>:ref:refs/heads/main`
- If workflow job sets `environment: production`, use `repo:<org>/<repo>:environment:production`

### ❌ "ParentResourceNotFound" for userAssignedIdentities/federatedIdentityCredentials

**Cause**: Using managed-identity command with a service principal appId/clientId, or wrong managed identity name/resource group  
**Solution**: If your workflow uses `AZURE_CLIENT_ID` from a service principal/app registration, use `az ad app federated-credential create` (Step 2). Only use `az identity federated-credential create` when you have an existing User Assigned Managed Identity.

---

## Environment Variable Injection

When GitHub Actions runs `azd provision` and `azd deploy`, environment values are passed to workloads:

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
