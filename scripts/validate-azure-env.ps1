#!/usr/bin/env pwsh

##
## Pre-deployment Azure Environment Validator (PowerShell)
## Checks for required Azure resources and creates them if needed
##

param(
    [Parameter(Mandatory=$true)]
    [string]$SubscriptionId,
    
    [Parameter(Mandatory=$false)]
    [string]$ResourceGroup = "aihub-rg",
    
    [Parameter(Mandatory=$false)]
    [string]$Location = "eastus"
)

Write-Host "🔍 Azure Environment Validation" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host "Subscription ID: $SubscriptionId"
Write-Host "Resource Group: $ResourceGroup"
Write-Host "Location: $Location"
Write-Host ""

try {
    # Set subscription
    Write-Host "📋 Setting active subscription..."
    az account set --subscription $SubscriptionId
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Failed to set subscription" -ForegroundColor Red
        exit 1
    }

    # Check if resource group exists
    Write-Host "🔍 Checking resource group..."
    $rgExists = az group exists --name $ResourceGroup | ConvertFrom-Json
    
    if ($rgExists) {
        Write-Host "✅ Resource group '$ResourceGroup' already exists" -ForegroundColor Green
    } else {
        Write-Host "📝 Creating resource group '$ResourceGroup'..."
        az group create --name $ResourceGroup --location $Location | Out-Null
        Write-Host "✅ Resource group created successfully" -ForegroundColor Green
    }

    # Check for SQL Server
    Write-Host "🔍 Checking SQL Server..."
    $sqlServers = az sql server list --resource-group $ResourceGroup --query "[0].name" -o tsv 2>$null
    
    if ([string]::IsNullOrEmpty($sqlServers) -or $sqlServers -eq "None") {
        Write-Host "⚠️  No SQL Server found. It will be provisioned by azd up" -ForegroundColor Yellow
    } else {
        Write-Host "✅ SQL Server '$sqlServers' exists" -ForegroundColor Green
        
        # Check for the database
        $database = az sql db list --resource-group $ResourceGroup --server-name $sqlServers `
            --query "[?name=='AiHubDb'].name" -o tsv 2>$null
        
        if ([string]::IsNullOrEmpty($database)) {
            Write-Host "⚠️  Database 'AiHubDb' not found. It will be created during provisioning" -ForegroundColor Yellow
        } else {
            Write-Host "✅ Database 'AiHubDb' exists on server '$sqlServers'" -ForegroundColor Green
        }
    }

    # Check for Container Registry
    Write-Host "🔍 Checking Container Registry..."
    $acr = az acr list --resource-group $ResourceGroup --query "[0].name" -o tsv 2>$null
    
    if ([string]::IsNullOrEmpty($acr) -or $acr -eq "None") {
        Write-Host "⚠️  No Container Registry found. It will be provisioned by azd up" -ForegroundColor Yellow
    } else {
        Write-Host "✅ Container Registry '$acr' exists" -ForegroundColor Green
    }

    # Check for Container Apps Environment
    Write-Host "🔍 Checking Container Apps Environment..."
    $acaEnv = az containerapp env list --resource-group $ResourceGroup --query "[0].name" -o tsv 2>$null
    
    if ([string]::IsNullOrEmpty($acaEnv) -or $acaEnv -eq "None") {
        Write-Host "⚠️  No Container Apps Environment found. It will be provisioned by azd up" -ForegroundColor Yellow
    } else {
        Write-Host "✅ Container Apps Environment '$acaEnv' exists" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "✅ Validation complete. Ready for deployment!" -ForegroundColor Green
    Write-Host ""
    Write-Host "📝 Next steps:" -ForegroundColor Cyan
    Write-Host "   1. Ensure AZURE_OPENAI_API_KEY, AZURE_OPENAI_MODEL_ID, and AZURE_OPENAI_ENDPOINT"
    Write-Host "      are set as GitHub Secrets (see docs/GitHub-Secrets-Setup.md)"
    Write-Host "   2. Run: azd up"
    Write-Host ""
}
catch {
    Write-Host "❌ Error during validation: $_" -ForegroundColor Red
    exit 1
}
