#!/usr/bin/env pwsh

# One-command local Entra auth bootstrap for Aspire dev (PowerShell).
# - Creates/reuses API + SPA app registrations
# - Configures API scope and SPA permissions
# - Writes ENTRA_* values into src/aspire/.env
#
# Prerequisites:
# - az CLI logged in (az login)
# - permissions to create/update app registrations in tenant

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Info {
    param([string]$Message)
    Write-Host "[entra-local] $Message"
}

function Write-WarnLine {
    param([string]$Message)
    Write-Warning "[entra-local] $Message"
}

function Test-CommandExists {
    param([string]$Name)
    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Get-AzTsv {
    param([string[]]$Arguments)

    $output = (& az @Arguments 2>$null)
    if ($LASTEXITCODE -ne 0) {
        return ""
    }

    if ($null -eq $output) {
        return ""
    }

    return ($output | Out-String).Trim()
}

function Invoke-Az {
    param([string[]]$Arguments)

    & az @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed: az $($Arguments -join ' ')"
    }
}

function Ensure-EnvFile {
    param(
        [string]$EnvFile,
        [string]$EnvExampleFile
    )

    $aspireDir = Split-Path -Parent $EnvFile
    if (-not (Test-Path -LiteralPath $aspireDir)) {
        New-Item -ItemType Directory -Path $aspireDir -Force | Out-Null
    }

    if (-not (Test-Path -LiteralPath $EnvFile) -and (Test-Path -LiteralPath $EnvExampleFile)) {
        Copy-Item -LiteralPath $EnvExampleFile -Destination $EnvFile
        Write-Info "Created $EnvFile from .env.example"
    }
}

function Ensure-App {
    param([string]$DisplayName)

    $existingAppId = Get-AzTsv @("ad", "app", "list", "--display-name", $DisplayName, "--query", "[0].appId", "-o", "tsv")
    if (-not [string]::IsNullOrWhiteSpace($existingAppId) -and $existingAppId -ne "None" -and $existingAppId -ne "null") {
        return $existingAppId
    }

    $createdAppId = Get-AzTsv @("ad", "app", "create", "--display-name", $DisplayName, "--sign-in-audience", "AzureADMyOrg", "--query", "appId", "-o", "tsv")
    if ([string]::IsNullOrWhiteSpace($createdAppId)) {
        throw "Failed to create app registration '$DisplayName'."
    }

    return $createdAppId
}

function Ensure-ServicePrincipal {
    param([string]$AppId)

    $existing = Get-AzTsv @("ad", "sp", "show", "--id", $AppId, "--query", "appId", "-o", "tsv")
    if ([string]::IsNullOrWhiteSpace($existing)) {
        Invoke-Az @("ad", "sp", "create", "--id", $AppId)
    }
}

function Get-AppObjectIdWithRetry {
    param(
        [string]$AppId,
        [int]$Attempts = 12,
        [int]$DelaySeconds = 5
    )

    for ($i = 1; $i -le $Attempts; $i++) {
        $objectId = Get-AzTsv @("ad", "app", "show", "--id", $AppId, "--query", "id", "-o", "tsv")
        if (-not [string]::IsNullOrWhiteSpace($objectId) -and $objectId -ne "None" -and $objectId -ne "null") {
            return $objectId
        }

        if ($i -lt $Attempts) {
            Write-Info "Waiting for Entra app propagation (attempt $i/$Attempts) for appId=$AppId..."
            [System.Threading.Thread]::Sleep($DelaySeconds * 1000)
        }
    }

    throw "Failed to resolve Entra application object id for appId=$AppId after $Attempts attempts."
}

function Ensure-Scope {
    param(
        [string]$ApiAppId,
        [string]$ApiObjectId
    )

    Invoke-Az @("ad", "app", "update", "--id", $ApiAppId, "--identifier-uris", "api://$ApiAppId")

    $existingScopeId = Get-AzTsv @("ad", "app", "show", "--id", $ApiAppId, "--query", "api.oauth2PermissionScopes[?value=='access_as_user'] | [0].id", "-o", "tsv")
    $scopeId = $existingScopeId
    if ([string]::IsNullOrWhiteSpace($scopeId) -or $scopeId -eq "None" -or $scopeId -eq "null") {
        $scopeId = [guid]::NewGuid().ToString().ToLowerInvariant()
    }

    $payload = @{
        api = @{
            requestedAccessTokenVersion = 2
            oauth2PermissionScopes = @(
                @{
                    id = $scopeId
                    adminConsentDescription = "Allow the app to access backend APIs on behalf of the signed in user."
                    adminConsentDisplayName = "Access backend API"
                    isEnabled = $true
                    type = "User"
                    userConsentDescription = "Allow the application to access backend APIs on your behalf."
                    userConsentDisplayName = "Access backend API"
                    value = "access_as_user"
                }
            )
        }
    }

    $payloadPath = Join-Path $env:TEMP "entra-api-scope-$($ApiAppId).json"
    $payload | ConvertTo-Json -Depth 10 | Set-Content -Path $payloadPath -Encoding utf8

    try {
        Invoke-Az @("rest", "--method", "PATCH", "--uri", "https://graph.microsoft.com/v1.0/applications/$ApiObjectId", "--headers", "Content-Type=application/json", "--body", "@$payloadPath")
    }
    finally {
        Remove-Item -LiteralPath $payloadPath -ErrorAction SilentlyContinue
    }

    return $scopeId
}

function Configure-SpaPermissions {
    param(
        [string]$SpaAppId,
        [string]$SpaObjectId,
        [string]$TargetApiAppId,
        [string]$ScopeId
    )

    if ([string]::IsNullOrWhiteSpace($ScopeId)) {
        throw "Scope id is empty; cannot configure SPA permissions."
    }

    $payload = @{
        requiredResourceAccess = @(
            @{
                resourceAppId = $TargetApiAppId
                resourceAccess = @(
                    @{
                        id = $ScopeId
                        type = "Scope"
                    }
                )
            }
        )
    }

    $payloadPath = Join-Path $env:TEMP "entra-spa-permissions-$($SpaAppId).json"
    $payload | ConvertTo-Json -Depth 10 | Set-Content -Path $payloadPath -Encoding utf8

    try {
        Invoke-Az "rest --method PATCH --uri https://graph.microsoft.com/v1.0/applications/$SpaObjectId --headers `"Content-Type=application/json`" --body `"@$payloadPath`""
    }
    finally {
        Remove-Item -LiteralPath $payloadPath -ErrorAction SilentlyContinue
    }

    & az ad app permission add --id $SpaAppId --api $TargetApiAppId --api-permissions "$ScopeId=Scope" | Out-Null

    & az ad app permission grant --id $SpaAppId --api $TargetApiAppId --scope access_as_user | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-WarnLine "'az ad app permission grant' failed for SPA app $SpaAppId. Tenant policy may require manual approval."
    }

    & az ad app permission admin-consent --id $SpaAppId | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-WarnLine "Admin consent did not succeed for SPA app $SpaAppId. Manual admin consent may be required for access_as_user."
    }
}

function Configure-SpaRedirects {
    param(
        [string]$SpaAppId,
        [string]$FrontendUrl
    )

    $urls = @(
        "http://localhost:3000",
        "https://localhost:3000",
        "http://localhost:3000/auth-callback.html",
        "https://localhost:3000/auth-callback.html"
    )

    if (-not [string]::IsNullOrWhiteSpace($FrontendUrl)) {
        $trimmed = $FrontendUrl.TrimEnd('/')
        $urls += $trimmed
        $urls += "$trimmed/auth-callback.html"
    }

    & az ad app update --id $SpaAppId --spa-redirect-uris @urls | Out-Null
    if ($LASTEXITCODE -eq 0) {
        return
    }

    $spaObjectId = Get-AppObjectIdWithRetry -AppId $SpaAppId
    $payload = @{ spa = @{ redirectUris = $urls } }
    $payloadPath = Join-Path $env:TEMP "entra-spa-redirects-$($SpaAppId).json"
    $payload | ConvertTo-Json -Depth 10 | Set-Content -Path $payloadPath -Encoding utf8

    try {
        Invoke-Az @("rest", "--method", "PATCH", "--uri", "https://graph.microsoft.com/v1.0/applications/$spaObjectId", "--headers", "Content-Type=application/json", "--body", "@$payloadPath")
    }
    finally {
        Remove-Item -LiteralPath $payloadPath -ErrorAction SilentlyContinue
    }
}

function Upsert-EnvVar {
    param(
        [string]$EnvFile,
        [string]$Key,
        [string]$Value
    )

    $lines = @()
    if (Test-Path -LiteralPath $EnvFile) {
        $lines = [System.IO.File]::ReadAllLines($EnvFile)
    }

    $prefix = "$Key="
    $updated = $false

    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i].StartsWith($prefix, [System.StringComparison]::Ordinal)) {
            $lines[$i] = "$Key=$Value"
            $updated = $true
            break
        }
    }

    if (-not $updated) {
        $lines += "$Key=$Value"
    }

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllLines($EnvFile, $lines, $utf8NoBom)
}

$rootDir = Split-Path -Parent $PSScriptRoot
$aspireDir = Join-Path $rootDir "src/aspire"
$envFile = Join-Path $aspireDir ".env"
$envExampleFile = Join-Path $aspireDir ".env.example"

if (-not (Test-CommandExists az)) {
    throw "Azure CLI (az) is required. Install and run 'az login' first."
}

& az account show | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "No active Azure login found. Run: az login"
}

Ensure-EnvFile -EnvFile $envFile -EnvExampleFile $envExampleFile

$tenantId = $env:AZURE_TENANT_ID
if ([string]::IsNullOrWhiteSpace($tenantId)) {
    $tenantId = Get-AzTsv @("account", "show", "--query", "tenantId", "-o", "tsv")
}
if ([string]::IsNullOrWhiteSpace($tenantId)) {
    throw "Unable to resolve tenant id. Set AZURE_TENANT_ID or ensure az account is configured."
}

$apiDisplayName = if ([string]::IsNullOrWhiteSpace($env:AZURE_ENTRA_API_APP_NAME)) { "aca-aspire-ai-local-api" } else { $env:AZURE_ENTRA_API_APP_NAME }
$spaDisplayName = if ([string]::IsNullOrWhiteSpace($env:AZURE_ENTRA_SPA_APP_NAME)) { "aca-aspire-ai-local-spa" } else { $env:AZURE_ENTRA_SPA_APP_NAME }
$frontendUrl = $env:FRONTEND_URL

$apiAppId = Ensure-App -DisplayName $apiDisplayName
$spaAppId = Ensure-App -DisplayName $spaDisplayName

$apiObjectId = Get-AppObjectIdWithRetry -AppId $apiAppId
$spaObjectId = Get-AppObjectIdWithRetry -AppId $spaAppId

Ensure-ServicePrincipal -AppId $apiAppId
Ensure-ServicePrincipal -AppId $spaAppId

$scopeId = Ensure-Scope -ApiAppId $apiAppId -ApiObjectId $apiObjectId
Configure-SpaPermissions -SpaAppId $spaAppId -SpaObjectId $spaObjectId -TargetApiAppId $apiAppId -ScopeId $scopeId
Configure-SpaRedirects -SpaAppId $spaAppId -FrontendUrl $frontendUrl

$entraAuthority = if ([string]::IsNullOrWhiteSpace($env:ENTRA_AUTHORITY)) { "https://login.microsoftonline.com/$tenantId/v2.0" } else { $env:ENTRA_AUTHORITY }
$entraAudience = if ([string]::IsNullOrWhiteSpace($env:ENTRA_AUDIENCE)) { "api://$apiAppId" } else { $env:ENTRA_AUDIENCE }
$entraScope = if ([string]::IsNullOrWhiteSpace($env:ENTRA_SCOPE)) { "api://$apiAppId/access_as_user" } else { $env:ENTRA_SCOPE }

Upsert-EnvVar -EnvFile $envFile -Key "ENTRA_AUTH_ENABLED" -Value "true"
Upsert-EnvVar -EnvFile $envFile -Key "ENTRA_TENANT_ID" -Value $tenantId
Upsert-EnvVar -EnvFile $envFile -Key "ENTRA_AUTHORITY" -Value $entraAuthority
Upsert-EnvVar -EnvFile $envFile -Key "ENTRA_API_CLIENT_ID" -Value $apiAppId
Upsert-EnvVar -EnvFile $envFile -Key "ENTRA_AUDIENCE" -Value $entraAudience
Upsert-EnvVar -EnvFile $envFile -Key "ENTRA_SPA_CLIENT_ID" -Value $spaAppId
Upsert-EnvVar -EnvFile $envFile -Key "ENTRA_SCOPE" -Value $entraScope

Write-Host ""
Write-Host "Local Entra auth bootstrap complete."
Write-Host "Updated $envFile with ENTRA_* values."
Write-Host "Now restart Aspire (F5) and use Sign in in the frontend."
