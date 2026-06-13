#!/usr/bin/env bash
set +e

echo "=== Supported regions: Microsoft.Search/searchServices ==="
az provider show --namespace Microsoft.Search --query "resourceTypes[?resourceType=='searchServices'].locations | [0]" -o table

echo "=== Supported regions: Microsoft.CognitiveServices/accounts ==="
az provider show --namespace Microsoft.CognitiveServices --query "resourceTypes[?resourceType=='accounts'].locations | [0]" -o table

echo "=== Supported regions: Microsoft.App/managedEnvironments ==="
az provider show --namespace Microsoft.App --query "resourceTypes[?resourceType=='managedEnvironments'].locations | [0]" -o table

echo "=== Supported regions: Microsoft.Sql/servers ==="
az provider show --namespace Microsoft.Sql --query "resourceTypes[?resourceType=='servers'].locations | [0]" -o table

echo "=== Supported regions: Microsoft.Storage/storageAccounts ==="
az provider show --namespace Microsoft.Storage --query "resourceTypes[?resourceType=='storageAccounts'].locations | [0]" -o table

echo "=== Canonical Azure region names ==="
az account list-locations --query "[].{name:name,display:displayName}" -o table
