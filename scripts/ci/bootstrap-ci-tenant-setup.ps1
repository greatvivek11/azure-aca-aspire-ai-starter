#!/usr/bin/env pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$DefaultResourceGroup = "azure-aca-aspire-ai-starter-rg"
$DefaultLocation = "southindia"
$DefaultSpName = "github-actions-copilot"
$DefaultGithubEnvironment = "dev"

$SubscriptionId = ""
$ResourceGroup = $DefaultResourceGroup
$Location = $DefaultLocation
$SpName = $DefaultSpName
$AzureClientId = ""
$GithubOwner = ""
$GithubRepo = ""
$GithubEnvironment = $DefaultGithubEnvironment

function Show-Usage {
    @"
Usage:
    powershell.exe -ExecutionPolicy Bypass -File scripts/ci/bootstrap-ci-tenant-setup.ps1 --subscription-id <id> --github-owner <owner> --github-repo <repo> [options]

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
    powershell.exe -ExecutionPolicy Bypass -File scripts/ci/bootstrap-ci-tenant-setup.ps1 --subscription-id "<sub-id>" --github-owner "my-org" --github-repo "azure-aca-aspire-ai-starter"

    powershell.exe -ExecutionPolicy Bypass -File scripts/ci/bootstrap-ci-tenant-setup.ps1 --subscription-id "<sub-id>" --resource-group "azure-aca-aspire-ai-starter-rg" --location "southindia" --github-owner "my-org" --github-repo "azure-aca-aspire-ai-starter" --github-environment "dev"
"@
}

function Get-RequiredValue {
    param(
        [string[]]$Arguments,
        [int]$Index,
        [string]$Name
    )

    if (($Index + 1) -ge $Arguments.Count) {
        throw "$Name requires a value."
    }

    return $Arguments[$Index + 1]
}

function Normalize-Output {
    param([string]$Value)

    if ($null -eq $Value) { return "" }

    $trimmed = $Value.Trim()
    if ($trimmed -eq "None" -or $trimmed -eq "null") { return "" }
    return $trimmed
}

function Invoke-AzTsv {
    param([string[]]$AzArgs)

    $out = (& az @AzArgs 2>$null)
    if ($LASTEXITCODE -ne 0) {
        return ""
    }

    return Normalize-Output (($out | Out-String).Trim())
}

function Invoke-Az {
    param([string[]]$AzArgs)

    & az @AzArgs | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed: az $($AzArgs -join ' ')"
    }
}

$argList = @($args)
for ($i = 0; $i -lt $argList.Count; ) {
    $arg = $argList[$i]
    switch ($arg) {
        "--subscription-id" {
            $SubscriptionId = Get-RequiredValue -Arguments $argList -Index $i -Name "--subscription-id"
            $i += 2
        }
        "--resource-group" {
            $ResourceGroup = Get-RequiredValue -Arguments $argList -Index $i -Name "--resource-group"
            $i += 2
        }
        "--location" {
            $Location = Get-RequiredValue -Arguments $argList -Index $i -Name "--location"
            $i += 2
        }
        "--sp-name" {
            $SpName = Get-RequiredValue -Arguments $argList -Index $i -Name "--sp-name"
            $i += 2
        }
        "--azure-client-id" {
            $AzureClientId = Get-RequiredValue -Arguments $argList -Index $i -Name "--azure-client-id"
            $i += 2
        }
        "--github-owner" {
            $GithubOwner = Get-RequiredValue -Arguments $argList -Index $i -Name "--github-owner"
            $i += 2
        }
        "--github-repo" {
            $GithubRepo = Get-RequiredValue -Arguments $argList -Index $i -Name "--github-repo"
            $i += 2
        }
        "--github-environment" {
            $GithubEnvironment = Get-RequiredValue -Arguments $argList -Index $i -Name "--github-environment"
            $i += 2
        }
        "-h" {
            Show-Usage
            exit 0
        }
        "--help" {
            Show-Usage
            exit 0
        }
        default {
            Write-Host "Unknown argument: $arg"
            Show-Usage
            exit 1
        }
    }
}

if ([string]::IsNullOrWhiteSpace($SubscriptionId)) {
    Write-Host "--subscription-id is required."
    Show-Usage
    exit 1
}

if ([string]::IsNullOrWhiteSpace($GithubOwner) -or [string]::IsNullOrWhiteSpace($GithubRepo)) {
    Write-Host "--github-owner and --github-repo are required."
    Show-Usage
    exit 1
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Error "Azure CLI (az) is required."
    exit 1
}

& az account show | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error "Run 'az login' first."
    exit 1
}

Invoke-Az @("account", "set", "--subscription", $SubscriptionId)
$TenantId = Invoke-AzTsv @("account", "show", "--query", "tenantId", "-o", "tsv")

Write-Host "[bootstrap] subscription: $SubscriptionId"
Write-Host "[bootstrap] tenant: $TenantId"
Write-Host "[bootstrap] resource group: $ResourceGroup"
Write-Host "[bootstrap] location: $Location"

Invoke-Az @(
    "group", "create",
    "--subscription", $SubscriptionId,
    "--name", $ResourceGroup,
    "--location", $Location
)

if ([string]::IsNullOrWhiteSpace($AzureClientId)) {
    $existingAppId = Invoke-AzTsv @("ad", "sp", "list", "--display-name", $SpName, "--query", "[0].appId", "-o", "tsv")
    if (-not [string]::IsNullOrWhiteSpace($existingAppId)) {
        $AzureClientId = $existingAppId
        Write-Host "[bootstrap] reusing existing service principal appId: $AzureClientId"
    }
    else {
        $AzureClientId = Invoke-AzTsv @(
            "ad", "sp", "create-for-rbac",
            "--name", $SpName,
            "--role", "Contributor",
            "--scopes", "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup",
            "--query", "appId",
            "-o", "tsv"
        )
        Write-Host "[bootstrap] created service principal appId: $AzureClientId"
    }
}
else {
    Write-Host "[bootstrap] using provided service principal appId: $AzureClientId"
}

$SpObjectId = Invoke-AzTsv @("ad", "sp", "show", "--id", $AzureClientId, "--query", "id", "-o", "tsv")
$scope = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup"

function Ensure-RbacRole {
    param([string]$RoleName)

    $existing = Invoke-AzTsv @(
        "role", "assignment", "list",
        "--assignee-object-id", $SpObjectId,
        "--scope", $scope,
        "--query", "[?roleDefinitionName=='$RoleName'] | [0].id",
        "-o", "tsv"
    )

    if (-not [string]::IsNullOrWhiteSpace($existing)) {
        Write-Host "[bootstrap] role already assigned: $RoleName"
        return
    }

    Invoke-Az @(
        "role", "assignment", "create",
        "--assignee-object-id", $SpObjectId,
        "--assignee-principal-type", "ServicePrincipal",
        "--role", $RoleName,
        "--scope", $scope
    )

    Write-Host "[bootstrap] role assigned: $RoleName"
}

Ensure-RbacRole -RoleName "Contributor"
Ensure-RbacRole -RoleName "User Access Administrator"

$cloudAppAdminRoleId = Invoke-AzTsv @(
    "rest",
    "--method", "GET",
    "--uri", "https://graph.microsoft.com/v1.0/roleManagement/directory/roleDefinitions?`$filter=displayName eq 'Cloud Application Administrator'",
    "--query", "value[0].id",
    "-o", "tsv"
)

if ([string]::IsNullOrWhiteSpace($cloudAppAdminRoleId)) {
    Write-Error "Failed to resolve Cloud Application Administrator role id."
    exit 1
}

$dirAssignmentExists = Invoke-AzTsv @(
    "rest",
    "--method", "GET",
    "--uri", "https://graph.microsoft.com/v1.0/roleManagement/directory/roleAssignments?`$filter=principalId eq '$SpObjectId' and roleDefinitionId eq '$cloudAppAdminRoleId'",
    "--query", "value[0].id",
    "-o", "tsv"
)

if (-not [string]::IsNullOrWhiteSpace($dirAssignmentExists)) {
    Write-Host "[bootstrap] Entra role already assigned: Cloud Application Administrator"
}
else {
    $body = @{ principalId = $SpObjectId; roleDefinitionId = $cloudAppAdminRoleId; directoryScopeId = "/" } | ConvertTo-Json -Compress
    Invoke-Az @(
        "rest",
        "--method", "POST",
        "--uri", "https://graph.microsoft.com/v1.0/roleManagement/directory/roleAssignments",
        "--headers", "Content-Type=application/json",
        "--body", $body
    )
    Write-Host "[bootstrap] Entra role assigned: Cloud Application Administrator"
}

$credName = "github-actions-env-$GithubEnvironment"
$credFile = [System.IO.Path]::GetTempFileName()
$credPayload = @{
    name = $credName
    issuer = "https://token.actions.githubusercontent.com"
    subject = "repo:$GithubOwner/$GithubRepo`:environment:$GithubEnvironment"
    description = "GitHub Actions environment $GithubEnvironment"
    audiences = @("api://AzureADTokenExchange")
} | ConvertTo-Json -Depth 6

Set-Content -Path $credFile -Value $credPayload -Encoding UTF8

try {
    $existingCred = Invoke-AzTsv @("ad", "app", "federated-credential", "list", "--id", $AzureClientId, "--query", "[?name=='$credName'].name", "-o", "tsv")
    if (-not [string]::IsNullOrWhiteSpace($existingCred)) {
        Invoke-Az @(
            "ad", "app", "federated-credential", "update",
            "--id", $AzureClientId,
            "--federated-credential-id", $credName,
            "--parameters", "@$credFile"
        )
        Write-Host "[bootstrap] federated credential updated: $credName"
    }
    else {
        Invoke-Az @(
            "ad", "app", "federated-credential", "create",
            "--id", $AzureClientId,
            "--parameters", "@$credFile"
        )
        Write-Host "[bootstrap] federated credential created: $credName"
    }
}
finally {
    Remove-Item -LiteralPath $credFile -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Bootstrap complete."
Write-Host "Set these GitHub secrets:"
Write-Host "  AZURE_SUBSCRIPTION_ID=$SubscriptionId"
Write-Host "  AZURE_CLIENT_ID=$AzureClientId"
Write-Host "  AZURE_TENANT_ID=$TenantId"
