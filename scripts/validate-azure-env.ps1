#!/usr/bin/env pwsh

##
## Pre-deployment Azure Environment Validator (PowerShell)
## Checks for required Azure resources and creates them if needed
##

param(
    [Parameter(Mandatory=$true)]
    [string]$SubscriptionId,
    
    [Parameter(Mandatory=$false)]
    [string]$ResourceGroup = "azure-aca-aspire-ai-starter-rg",
    
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
        Write-Host "⚠️  No Container Registry found. This is expected when using external/public images." -ForegroundColor Yellow
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

    # Check for Storage Account
    Write-Host "🔍 Checking Storage Account..."
    $storageAccount = az storage account list --resource-group $ResourceGroup --query "[0].name" -o tsv 2>$null

    if ([string]::IsNullOrEmpty($storageAccount) -or $storageAccount -eq "None") {
        Write-Host "⚠️  No Storage Account found. It will be provisioned by azd provision." -ForegroundColor Yellow
    } else {
        Write-Host "✅ Storage Account '$storageAccount' exists" -ForegroundColor Green
    }

    # Check for Azure AI Search
    Write-Host "🔍 Checking Azure AI Search..."
    $searchService = az search service list --resource-group $ResourceGroup --query "[0].name" -o tsv 2>$null

    if ([string]::IsNullOrEmpty($searchService) -or $searchService -eq "None") {
        Write-Host "⚠️  No Azure AI Search service found. It will be provisioned by azd provision." -ForegroundColor Yellow
    } else {
        Write-Host "✅ Azure AI Search '$searchService' exists" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "✅ Validation complete. Ready for deployment!" -ForegroundColor Green
    Write-Host ""
    Write-Host "📝 Next steps:" -ForegroundColor Cyan
    Write-Host "   1. Ensure OpenAI runtime or provisioning secrets are configured (see docs/GitHub-Secrets-Setup.md)"
    Write-Host "   2. Run the GitHub Actions deployment workflow or azd provision/azd deploy as needed"
    Write-Host ""
}
catch {
    Write-Host "❌ Error during validation: $_" -ForegroundColor Red
    exit 1
}
