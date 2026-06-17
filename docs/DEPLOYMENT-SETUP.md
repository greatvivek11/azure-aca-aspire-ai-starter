# CI/CD, Testing & Deployment Setup - Summary

This document summarizes all the changes made to enable automated GitHub Actions deployment, architecture testing, and pre-deployment validation.

> Source-of-truth note:
> Treat this file as an operational summary. The canonical implementation is the workflow YAML in `.github/workflows/` and the scripts under `scripts/ci/`.
> If this summary differs from workflow or script behavior, follow code.

## 📋 Summary of Changes

### 1. **GitHub Actions Workflow** ✅
**Entry workflow**: `.github/workflows/deploy.yml`

**Reusable workflows**:
- `.github/workflows/reusable-validate-build.yml`
- `.github/workflows/reusable-deploy-azure.yml`

A complete CI/CD pipeline that:
- ✅ Triggers on push to `main` or manual dispatch
- ✅ Validates code (build + tests)
- ✅ Deploys to Azure Container Apps with **public GHCR images by default**
- ✅ Supports **managed ACR** as an opt-in deployment mode
- ✅ Authenticates via OIDC (no stored credentials)
- ✅ Uses Managed Identity for backend SQL runtime auth

**Two-job flow:**
1. **Validate Job**: Build, test, quality checks (~2-3 min)
2. **Deploy Job**: Authenticate, validate env, deploy (~5-10 min)

---

### 2. **Architecture Tests** ✅
**Files**: 
- `src/Backend.Tests/Backend.Tests.csproj` (new test project)
- `src/Backend.Tests/DependencyArchitectureTests.cs` (test cases)

**Test coverage**:
- Backend project structure validation
- Architecture test infrastructure

**Usage**:
```bash
# Run locally
dotnet test src/Backend.Tests/Backend.Tests.csproj

# Run in CI/CD (automatic)
dotnet test src/Backend.Tests/Backend.Tests.csproj --configuration Release
```

**Future extensions**: Add reflection-based tests for dependency validation as codebase grows

---

### 3. **Pre-deployment Validation Scripts** ✅
**Files**:
- `scripts/validate-azure-env.sh` (Bash)
- `scripts/validate-azure-env.ps1` (PowerShell)

**What they check**:
- ✅ Resource group exists (creates if missing)
- ✅ SQL Server/Database status
- ✅ Container Registry status (optional when using external/public images)
- ✅ Container Apps Environment status

**Usage**:
```bash
# Bash
bash scripts/validate-azure-env.sh <SUBSCRIPTION_ID> azure-aca-aspire-ai-starter-rg

# PowerShell
pwsh scripts/validate-azure-env.ps1 -SubscriptionId <SUBSCRIPTION_ID> -ResourceGroup azure-aca-aspire-ai-starter-rg
```

---

### 4. **Documentation** ✅
**New docs created**:

| File | Purpose |
|------|---------|
| [GitHub-Secrets-Setup.md](./GitHub-Secrets-Setup.md) | Configure GitHub Secrets for CI/CD |
| [CI-CD-GitHub-Actions.md](./CI-CD-GitHub-Actions.md) | GitHub Actions workflow deep-dive |
| [Architecture-Tests.md](./Architecture-Tests.md) | Architecture testing patterns & examples |
| [README.md](./README.md) | Updated with deployment section |

---

### 5. **Solution Updates** ✅
**Files**:
- `azure-aca-aspire-ai-starter.sln` (Backend.Tests project added)

Backend.Tests project is now part of the build and test pipeline

---

## 🚀 Deployment Workflow

### Local Development
```bash
# 1. Make changes
git add .
git commit -m "Feature: Add something"

# 2. Push to main
git push origin main
```

### GitHub Actions (Automatic)
1. Webhook triggers `deploy.yml` workflow
2. **Validate Job** runs:
   - Builds backend + frontend
   - Runs architecture tests
   - Reports status
3. **Deploy Job** runs (if validation passes):
   - Authenticates to Azure
  - Ensures Entra API + SPA app registrations and delegated scope wiring
   - Validates environment
  - In external mode, builds and pushes public GHCR images, then runs `azd provision`
  - In managed mode, provisions ACR-backed infra and deploys each service with `azd deploy`
  - Bicep injects non-secret SQL runtime config (`SQL_SERVER`, `SQL_DATABASE`, `AZURE_CLIENT_ID`)
  - Bicep configures frontend as public ingress and keeps backend/worker private in ACA
4. ✅ Deployment complete!

Monitor progress: **GitHub → Actions → Deploy to Azure Container Apps**

---

## 🔐 GitHub Secrets Configuration

**Required secrets** (go to Settings → Secrets and variables → Actions):

```
AZURE_SUBSCRIPTION_ID       (your subscription ID)
AZURE_CLIENT_ID             (service principal client ID)
AZURE_TENANT_ID             (Entra ID tenant ID)
AI_SERVICES_PROVISIONING_MODE (optional; defaults to provision)
AZURE_OPENAI_API_KEY        (required only when AI_SERVICES_PROVISIONING_MODE=external)
AZURE_OPENAI_MODEL_ID       (required only when AI_SERVICES_PROVISIONING_MODE=external)
AZURE_OPENAI_ENDPOINT       (required only when AI_SERVICES_PROVISIONING_MODE=external)
AZURE_SQL_ADMIN_LOGIN       (provision-time SQL admin login)
AZURE_SQL_ADMIN_PASSWORD    (provision-time SQL admin password)
ENTRA_AUTH_ENABLED          (optional: true|false, default: true)
AZD_ENVIRONMENT_NAME        (optional, default: azure-aca-aspire-ai-starter)
```

Optional override only (advanced):
```
AZURE_SQL_ENTRA_ADMIN_LOGIN
AZURE_SQL_ENTRA_ADMIN_OBJECT_ID
CONTAINER_REGISTRY_MODE     (optional: external|managed, default: external)
EXTERNAL_REGISTRY_SERVER    (optional, default: ghcr.io)
EXTERNAL_REGISTRY_USERNAME  (optional; needed for authenticated non-GHCR registries)
EXTERNAL_REGISTRY_PASSWORD  (optional; needed for authenticated non-GHCR registries)
ENABLE_LOG_ANALYTICS        (optional: true|false, default: false)
ENABLE_ASPIRE_DASHBOARD     (optional: true|false, default: true)
BACKEND_MIN_REPLICAS        (optional, default: 0)
FRONTEND_MIN_REPLICAS       (optional, default: 0)
WORKER_MIN_REPLICAS         (optional, default: 0)
ENTRA_AUTHORITY             (optional authority override)
ENTRA_API_CLIENT_ID         (optional existing API app client ID)
ENTRA_AUDIENCE              (optional audience override)
ENTRA_SPA_CLIENT_ID         (optional existing SPA app client ID)
ENTRA_SCOPE                 (optional scope override)
AZURE_ENTRA_API_APP_NAME    (optional API app display name)
AZURE_ENTRA_SPA_APP_NAME    (optional SPA app display name)
```

**Setup instructions**: See [GitHub-Secrets-Setup.md](./GitHub-Secrets-Setup.md)

---

## ✅ Pre-deployment Checklist

Before committing and pushing:

- [ ] All GitHub Secrets configured
- [ ] Local build succeeds: `dotnet build azure-aca-aspire-ai-starter.sln`
- [ ] Architecture tests pass: `dotnet test src/Backend.Tests/Backend.Tests.csproj`
- [ ] Frontend builds: `npm run build --prefix src/frontend`
- [ ] Code committed: `git push origin main`

After push:

- [ ] Monitor GitHub Actions workflow
- [ ] Verify deployment in Azure Portal
- [ ] Test deployed app endpoints

---

## 🔧 Customization

### Modify Deployment Triggers
Edit `.github/workflows/deploy.yml`:

```yaml
on:
  push:
    branches:
      - main                    # Change branch
  schedule:
    - cron: '0 2 * * *'        # Add scheduled deployment
```

### Add More Tests
Create test file in `src/Backend.Tests/` and it will automatically run in CI/CD.

### Update Validation Script
Edit `scripts/validate-azure-env.sh` or `.ps1` to add more checks.

---

## 🆘 Troubleshooting

| Issue | Solution |
|-------|----------|
| **Secrets not found in workflow** | Go to Settings → Secrets and verify all secrets are configured |
| **"OIDC authentication failed"** | Check that service principal has Contributor role on resource group |
| **"azd not logged in"** | Add `azd config set auth.useAzCliAuth true` after `azure/login` in workflow |
| **"azd provision/deploy failed"** | Check `AZD_ENVIRONMENT_NAME` environment variable; run validation script first |
| **External registry push/login failed** | For GHCR, keep `CONTAINER_REGISTRY_MODE=external` and let the workflow use `GITHUB_TOKEN`; for other registries, set `EXTERNAL_REGISTRY_USERNAME` and `EXTERNAL_REGISTRY_PASSWORD` |
| **Aspire Dashboard provisioning fails** | Set `ENABLE_ASPIRE_DASHBOARD=false` and redeploy if preview `dotNetComponents` is unavailable in your region/subscription |
| **`AppLogsConfiguration.Destination` invalid when Aspire Dashboard is enabled** | Keep `ENABLE_LOG_ANALYTICS=true`, or leave it `false` and use the updated template that automatically switches destination to `azure-monitor` when Aspire Dashboard is on |
| **Tests fail in CI but pass locally** | Ensure Release configuration: `dotnet test --configuration Release` |
| **Deployment times out** | Check Azure Portal → Container Apps for errors; review Dapr sidecars |

---

## 📚 Next Steps

1. ✅ Configure GitHub Secrets (see [GitHub-Secrets-Setup.md](./GitHub-Secrets-Setup.md))
2. ✅ Commit all changes: `git push origin main`
3. ⏭️ Monitor GitHub Actions deployment
4. ⏭️ Test the deployed application
5. ⏭️ Set up continuous monitoring (Application Insights, etc.)

---

## 🎯 Future Enhancements

- [ ] Add code coverage metrics to tests
- [ ] Add integration tests for API endpoints
- [ ] Add performance benchmarks
- [ ] Add security scanning (Snyk, Trivy)
- [ ] Add dependency version scanning
- [ ] Add automated rollback on deployment failure
- [ ] Add approval gates for production deployments

---

## 📖 References

- [GitHub Secrets Setup](./GitHub-Secrets-Setup.md)
- [CI/CD Workflow Details](./CI-CD-GitHub-Actions.md)
- [Architecture Tests Guide](./Architecture-Tests.md)
- [Azure Developer CLI (azd)](https://github.com/Azure/azure-dev)
- [GitHub Actions Documentation](https://docs.github.com/en/actions)
