#!/bin/bash

##
## Pre-deployment Azure Environment Validator
## Checks for required Azure resources and creates them if needed
##

set -euo pipefail

SUBSCRIPTION_ID="${1:-}"
RESOURCE_GROUP="${2:-azure-aca-aspire-ai-starter-rg}"
LOCATION="${3:-eastus}"

if [ -z "$SUBSCRIPTION_ID" ]; then
    echo "❌ Error: SUBSCRIPTION_ID is required"
    echo "Usage: validate-azure-env.sh <SUBSCRIPTION_ID> [RESOURCE_GROUP] [LOCATION]"
    exit 1
fi

echo "🔍 Azure Environment Validation"
echo "================================"
echo "Subscription ID: $SUBSCRIPTION_ID"
echo "Resource Group: $RESOURCE_GROUP"
echo "Location: $LOCATION"
echo ""

# Set subscription
echo "📋 Setting active subscription..."
az account set --subscription "$SUBSCRIPTION_ID" || {
    echo "❌ Failed to set subscription"
    exit 1
}

# Check if resource group exists
echo "🔍 Checking resource group..."
if az group exists --name "$RESOURCE_GROUP" | grep -q "true"; then
    echo "✅ Resource group '$RESOURCE_GROUP' already exists"
else
    echo "📝 Creating resource group '$RESOURCE_GROUP'..."
    az group create \
        --name "$RESOURCE_GROUP" \
        --location "$LOCATION" > /dev/null
    echo "✅ Resource group created successfully"
fi

# Check for SQL Server
echo "🔍 Checking SQL Server..."
SQL_SERVER=$(az sql server list --resource-group "$RESOURCE_GROUP" --query "[0].name" -o tsv 2>/dev/null || echo "")

if [ -z "$SQL_SERVER" ] || [ "$SQL_SERVER" == "None" ]; then
    echo "⚠️  No SQL Server found. It will be provisioned by azd up"
else
    echo "✅ SQL Server '$SQL_SERVER' exists"
    
    # Check for the database
    DATABASE=$(az sql db list --resource-group "$RESOURCE_GROUP" --server-name "$SQL_SERVER" \
        --query "[?name=='acaaspireaistarter'].name" -o tsv 2>/dev/null || echo "")
    
    if [ -z "$DATABASE" ]; then
        echo "⚠️  Database 'acaaspireaistarter' not found. It will be created during provisioning"
    else
        echo "✅ Database 'acaaspireaistarter' exists on server '$SQL_SERVER'"
    fi
fi

# Check for Container Registry
echo "🔍 Checking Container Registry..."
ACR=$(az acr list --resource-group "$RESOURCE_GROUP" --query "[0].name" -o tsv 2>/dev/null || echo "")

if [ -z "$ACR" ] || [ "$ACR" == "None" ]; then
    echo "⚠️  No Container Registry found. This is expected when using external/public images."
else
    echo "✅ Container Registry '$ACR' exists"
fi

# Check for Container Apps Environment
echo "🔍 Checking Container Apps Environment..."
ACA_ENV=$(az containerapp env list --resource-group "$RESOURCE_GROUP" --query "[0].name" -o tsv 2>/dev/null || echo "")

if [ -z "$ACA_ENV" ] || [ "$ACA_ENV" == "None" ]; then
    echo "⚠️  No Container Apps Environment found. It will be provisioned by azd up"
else
    echo "✅ Container Apps Environment '$ACA_ENV' exists"
fi

# Check for Storage Account
echo "🔍 Checking Storage Account..."
STORAGE_ACCOUNT=$(az storage account list --resource-group "$RESOURCE_GROUP" --query "[0].name" -o tsv 2>/dev/null || echo "")

if [ -z "$STORAGE_ACCOUNT" ] || [ "$STORAGE_ACCOUNT" == "None" ]; then
    echo "⚠️  No Storage Account found. It will be provisioned by azd provision."
else
    echo "✅ Storage Account '$STORAGE_ACCOUNT' exists"
fi

# Check for Azure AI Search
echo "🔍 Checking Azure AI Search..."
SEARCH_SERVICE=$(az search service list --resource-group "$RESOURCE_GROUP" --query "[0].name" -o tsv 2>/dev/null || echo "")

if [ -z "$SEARCH_SERVICE" ] || [ "$SEARCH_SERVICE" == "None" ]; then
    echo "⚠️  No Azure AI Search service found. It will be provisioned by azd provision."
else
    echo "✅ Azure AI Search '$SEARCH_SERVICE' exists"
fi

echo ""
echo "✅ Validation complete. Ready for deployment!"
echo ""
echo "📝 Next steps:"
echo "   1. Ensure OpenAI runtime or provisioning secrets are configured (see docs/GitHub-Secrets-Setup.md)"
echo "   2. Run the GitHub Actions deployment workflow or azd provision/azd deploy as needed"
echo ""
