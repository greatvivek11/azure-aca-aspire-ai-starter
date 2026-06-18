# CI/CD & GitHub Actions Deployment

This document describes the automated deployment pipeline for the ACA Aspire AI Starter project using GitHub Actions and Azure Developer CLI (azd).

> Source-of-truth note:
> The authoritative implementation is in `.github/workflows/deploy.yml`, `.github/workflows/reusable-validate-build.yml`, and `.github/workflows/reusable-deploy-azure.yml`.
> If this document and workflow YAML differ, follow the workflow YAML.

## Overview

The CI/CD pipeline automates:
- ✅ Building and testing the entire solution
- ✅ Running architecture tests to ensure code quality
- ✅ Validating Azure infrastructure prerequisites
- ✅ Deploying to Azure Container Apps using `azd provision` + `azd deploy`
- ✅ Using Managed Identity for backend SQL runtime authentication
- ✅ Bootstrapping and finalizing Entra app registration wiring for SPA + API auth during deployment

**Deployment triggers:**
- Push to `main` branch (automatic; deploys only if code/config changed, not for doc-only changes)
- Pull requests targeting `main` (validates all changes; deploy job skipped)
- Manual trigger via GitHub Actions UI with environment selection (`dev`, `staging`, `prod`)

**Smart doc-only handling:** Changes to files under `/docs/` or `README.md` only skip the expensive deploy job. Validation (build, test, lint) still runs, so you catch code issues early even on doc updates.

---

## Workflow Architecture

```
┌──────────────────────────────────────────────────────────────┐
│ GitHub Actions Workflow: deploy.yml                          │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  Job 1: CHECK-CHANGES (detect non-docs files changed)       │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ Compares base/head commits to identify if any files    │ │
│  │ outside /docs/ or README.md were modified.             │ │
│  │ Outputs: has-non-docs-changes (true|false)             │ │
│  └────────────────────────────────────────────────────────┘ │
│                       ↓ (parallel with validate)            │
│  Job 2: VALIDATE & BUILD (runs on ubuntu-latest)           │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ 1. Checkout code                                       │ │
│  │ 2. Setup .NET 10 SDK                                   │ │
│  │ 3. Setup Node.js 20                                    │ │
│  │ 4. Restore .NET dependencies                           │ │
│  │ 5. Build Backend (dotnet build)                        │ │
│  │ 6. Run Architecture Tests (xUnit)                      │ │
│  │ 7. Build Frontend (npm install + npm run build)        │ │
│  │ 8. Upload build artifacts                              │ │
│  └────────────────────────────────────────────────────────┘ │
│                 ↓ (depends on: validate + check-changes)    │
│  Job 3: DEPLOY (runs ONLY if non-docs files changed)       │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ 1. Checkout code                                       │ │
│  │ 2. Setup Azure Developer CLI (azd)                     │ │
│  │ 3. Setup .NET 10 SDK                                   │ │
│  │ 4. Setup Node.js 20                                    │ │
│  │ 5. Authenticate to Azure via OIDC                      │ │
│  │ 6. Configure azd to use Azure CLI auth                 │ │
│  │ 7. Validate Azure environment                          │ │
│  │ 8. Run: azd provision --no-prompt                      │ │
│  │ 9. Run: azd deploy <service> --no-prompt               │ │
│  │ 10. Display deployment summary                         │ │
│  └────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
```

---

## Workflow File Structure

Entry workflow: `.github/workflows/deploy.yml`

Reusable workflows:
- `.github/workflows/reusable-validate-build.yml`
- `.github/workflows/reusable-deploy-azure.yml`

### 1. Validation & Build Job

**Purpose**: Compile code, run tests, ensure quality before deployment

```yaml
validate:
  name: Validate and Build
  runs-on: ubuntu-latest
  steps:
    - Setup tools (.NET, Node.js)
    - Build Backend (dotnet build)
    - Run Architecture Tests (xUnit)
    - Build Frontend (Vite)
    - Upload artifacts
```

**Artifacts uploaded**: Build outputs for Backend, Frontend, Aspire, Worker projects

### 2. Deployment Job

**Purpose**: Deploy to Azure Container Apps using `azd provision` and service-level `azd deploy`

```yaml
deploy:
  name: Deploy to Azure
  needs: validate  # Wait for validation job to complete
  runs-on: ubuntu-latest
  environment:
    name: dev  # workflow_dispatch can override via input
  steps:
    - Setup tools (azd, .NET, Node.js)
    - Authenticate to Azure (OpenID Connect)
    - Preflight deploy/auth validation (includes Entra input validation)
    - Entra bootstrap (idempotent app registration + scope wiring)
    - Configure azd to use Azure CLI auth
    - Validate Azure environment
    - Run: azd provision --no-prompt
    - Run: azd deploy backend/frontend/worker --no-prompt
    - Entra finalize (redirect URI and app metadata finalization)
    - Display deployment summary
```

---

## Environment Variables & Secrets

### Required GitHub Secrets

These must be configured in **Settings → Secrets and variables → Actions**:

```
AZURE_SUBSCRIPTION_ID       # Azure subscription ID
AZURE_CLIENT_ID             # Service Principal client ID
AZURE_TENANT_ID             # Azure Entra ID tenant ID
AI_SERVICES_PROVISIONING_MODE  # Optional: provision (default) or external
AZURE_OPENAI_API_KEY        # Required only when AI_SERVICES_PROVISIONING_MODE=external
AZURE_OPENAI_MODEL_ID       # Required only when AI_SERVICES_PROVISIONING_MODE=external
AZURE_OPENAI_ENDPOINT       # Required only when AI_SERVICES_PROVISIONING_MODE=external
AZD_ENVIRONMENT_NAME        # Optional: environment name (default: azure-aca-aspire-ai-starter)
```

**See**: [GitHub-Secrets-Setup.md](./GitHub-Secrets-Setup.md) for detailed configuration

### Required GitHub Environment

The deploy job runs with a GitHub Environment (default: `dev`).
Create it before first deployment:

```bash
gh api \
  --method PUT \
  -H "Accept: application/vnd.github+json" \
  /repos/<owner>/<repo>/environments/dev
```

Environment secrets are optional. Repository secrets still work.

### Workflow Environment Variables

```yaml
AZURE_SUBSCRIPTION_ID: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
AZURE_CLIENT_ID: ${{ secrets.AZURE_CLIENT_ID }}
AZURE_TENANT_ID: ${{ secrets.AZURE_TENANT_ID }}
AZD_ENVIRONMENT_NAME: azure-aca-aspire-ai-starter
```

### Azure OpenAI Secrets (Injected at Deploy Time)

```yaml
env:
  AZURE_OPENAI_API_KEY: ${{ secrets.AZURE_OPENAI_API_KEY }}
  AZURE_OPENAI_MODEL_ID: ${{ secrets.AZURE_OPENAI_MODEL_ID }}
  AZURE_OPENAI_ENDPOINT: ${{ secrets.AZURE_OPENAI_ENDPOINT }}
```

These become Container Apps environment variables and are passed to the backend container at runtime.

---

## Azure Authentication (OIDC)

`azure/login` authenticates Azure CLI for the current job. `azd` uses its own auth
mode unless configured, so the workflow sets:

```bash
azd config set auth.useAzCliAuth true
```

This makes `azd` commands reuse the Azure CLI OIDC session from `azure/login`.

The workflow uses **OpenID Connect (OIDC)** with `azure/login@v2` instead of storing credentials:

```yaml
- name: Log in to Azure using OIDC
  uses: azure/login@v2
  with:
    client-id: ${{ secrets.AZURE_CLIENT_ID }}
    tenant-id: ${{ secrets.AZURE_TENANT_ID }}
    subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
```

**Benefits:**
- ✅ No stored credentials in GitHub
- ✅ Short-lived tokens (expires in ~1 hour)
- ✅ Audit trail in Azure Activity Log
- ✅ Follows security best practices

OIDC subject must match GitHub Environment mode:

- `repo:<owner>/<repo>:environment:dev` (default)
- `repo:<owner>/<repo>:environment:staging`
- `repo:<owner>/<repo>:environment:prod`

Use one federated credential per environment subject.

**Setup**: See [GitHub-Secrets-Setup.md](./GitHub-Secrets-Setup.md) → "Configure OIDC Trust"

### Required RBAC for CI Principal

The workflow provisions infrastructure that includes role assignments for
Container Apps to pull from ACR using managed identity. The CI service principal
must therefore be allowed to create role assignments at deployment scope.

Required on the target resource group:
- `Contributor`
- `User Access Administrator`

Alternative:
- `Owner`

If this is missing, `azd provision` fails during validation with an error similar
to: `Microsoft.Authorization/roleAssignments/write` not permitted.

---

## Pre-deployment Validation

Before deploying, the workflow runs a validation script:

```bash
scripts/validate-azure-env.sh \
  "${{ secrets.AZURE_SUBSCRIPTION_ID }}" \
  "azure-aca-aspire-ai-starter-rg"
```

**Checks:**
- ✅ Resource group exists (creates if missing)
- ✅ SQL Server exists (warns if not)
- ✅ Container Registry exists (warns if not)
- ✅ Container Apps Environment exists (warns if not)

**Scripts available in:**
- `scripts/validate-azure-env.sh` (Bash)
- `scripts/validate-azure-env.ps1` (PowerShell)

---

## Architecture Tests

The validation job runs **Architecture Tests** to ensure code quality:

```bash
dotnet test src/Backend.Tests/Backend.Tests.csproj --configuration Release
```

**What tests check:**
- Expected feature slices exist (`Health`, `AiPing`, `Customers`, `DocumentIngestion`, `Chat`)
- Features are independent (no cross-feature coupling)
- Auth middleware is wired (`UseAuthentication`, `UseAuthorization`, `UseRateLimiter`, `AddJwtBearer`)
- Auth behavior and endpoint contracts hold (integration tests)

**Test implementation**: See [Architecture-Tests.md](./Architecture-Tests.md)

---

## Deployment Flow

### 1. Push to Main or Manual Trigger

```bash
git push origin main
```

### 2. GitHub Actions Triggered

- **Validation Job** starts (typically 2-3 minutes)
- Tests, builds, and validates code
- If any test fails, deployment is blocked

### 3. Deployment Job (Conditional)

- Starts only if validation succeeds
- Authenticates to Azure
- Validates infrastructure
- Runs `azd provision` followed by `azd deploy` per service
- Uses Managed Identity for backend SQL runtime authentication

### 4. Deployment Summary

```
✅ Deployment successful!
Environment: azure-aca-aspire-ai-starter
Subscription: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
```

---

## Monitoring Deployment

### View Workflow Status

Go to: **Actions → Deploy to Azure Container Apps**

Each run shows:
- Commit message
- Status (✅ Success, ❌ Failed, ⏳ In Progress)
- Runtime duration
- Log details for each job

### Check Logs

Click on a workflow run → View detailed logs for:
- Build output
- Test results
- Deployment progress
- Error messages (if any)

### View Azure Resources

After deployment completes:

```bash
# List deployed container apps
az containerapp list --resource-group azure-aca-aspire-ai-starter-rg --query "[].{name:name, status:properties.runStatus}"

# View container app logs
az containerapp logs show --name web --resource-group azure-aca-aspire-ai-starter-rg
```

---

## Troubleshooting

### ❌ Validation Job Failed

**Check**: Build or test errors in logs

```bash
# Run locally to debug
dotnet build azure-aca-aspire-ai-starter.sln
dotnet test src/Backend.Tests/Backend.Tests.csproj
npm run build --prefix src/frontend
```

### ❌ Deployment Job Failed at Authentication

**Check**: Azure credentials (OIDC setup)

Most common root cause after switching to environments: subject mismatch.
If deploy job uses environment `dev`, federated credential subject must be:
`repo:<owner>/<repo>:environment:dev`

```bash
# Verify service principal
az ad sp show --id <AZURE_CLIENT_ID>

# Verify OIDC federation
az identity federated-credential list --resource-group azure-aca-aspire-ai-starter-rg
```

### ❌ "Azure OpenAI credentials not found"

**Check**: GitHub Secrets are configured

```bash
gh secret list --repo <owner>/<repo>
```

### ❌ "Insufficient permissions" error

**Check**: Service Principal has Contributor role

```bash
az role assignment list --assignee <AZURE_CLIENT_ID>
```

---

## Local Testing

To test the workflow logic locally before pushing:

```bash
# Build backend
dotnet build src/backend/Backend.csproj --configuration Release

# Run architecture tests
dotnet test src/Backend.Tests/Backend.Tests.csproj

# Build frontend
npm run build --prefix src/frontend

# Test validation script
bash scripts/validate-azure-env.sh <SUBSCRIPTION_ID> azure-aca-aspire-ai-starter-rg
```

---

## Next Steps

1. ✅ Configure GitHub Secrets: [GitHub-Secrets-Setup.md](./GitHub-Secrets-Setup.md)
2. ✅ Commit code: `git push origin main`
3. ⏭️ Monitor deployment: **Actions** tab
4. ⏭️ Verify deployed app in Azure Portal

---

## Advanced Configuration

### Manual Approval Before Deployment

Add to `.github/workflows/deploy.yml`:

```yaml
deploy:
  # environment: production
  # Note: If enabled, configure OIDC subject as
  # repo:<org>/<repo>:environment:production in Entra ID
```

### Scheduled Deployments

Deploy on a schedule:

```yaml
on:
  schedule:
    - cron: '0 2 * * *'  # Daily at 2 AM UTC
```

### Multiple Environments

```yaml
on:
  workflow_dispatch:
    inputs:
      environment:
        description: 'Deployment environment'
        required: true
        options:
          - dev
          - staging
          - prod
```

---

## References

- [Azure Developer CLI (azd)](https://github.com/Azure/azure-dev)
- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [OpenID Connect in GitHub Actions](https://docs.github.com/en/actions/deployment/security-hardening-your-deployments/about-security-hardening-with-openid-connect)
- [Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/)
