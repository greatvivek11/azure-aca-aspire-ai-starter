targetScope = 'resourceGroup'

@description('Primary deployment location for all resources.')
param location string = resourceGroup().location

@description('Environment name for resource naming and tagging.')
param environmentName string = 'dev'

@description('Optional tags applied to all resources.')
param tags object = {}

@description('SQL admin login for Azure SQL server.')
param sqlAdminLogin string

@secure()
@description('SQL admin password for Azure SQL server.')
param sqlAdminPassword string

@description('Azure SQL database name used by backend API.')
param sqlDatabaseName string = 'copilotsk'

@allowed([
  'provision'
  'existing'
])
@description('SQL strategy: provision creates SQL resources via IaC; existing uses pre-created SQL resources.')
param sqlProvisioningMode string = 'provision'

@description('Existing Azure SQL server name (without .database.windows.net). Required when sqlProvisioningMode is existing.')
param existingSqlServerName string = ''

@description('Existing Azure SQL database name. Required when sqlProvisioningMode is existing.')
param existingSqlDatabaseName string = ''
@description('Entra AD admin login (email) for Azure SQL server. Optional.')
param sqlEntraAdminLogin string = ''

@description('Entra AD admin object ID for Azure SQL server. Required if sqlEntraAdminLogin is provided.')
param sqlEntraAdminObjectId string = ''
var baseName = 'aih-${environmentName}-${uniqueString(resourceGroup().id)}'
var acrPullRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '7f951dda-4ed3-4680-a7ca-43fe172d538d'
)
var defaultContainerImage = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
var shortId = take(uniqueString(resourceGroup().id), 6)
var backendAppName = 'backend-${shortId}'
var frontendAppName = 'frontend-${shortId}'
var workerAppName = 'worker-${shortId}'
var containerAppsManagedIdentityName = '${baseName}-cai'
var sqlServerName = take('${baseName}-sql', 63)
var useExistingSql = toLower(sqlProvisioningMode) == 'existing'
var resolvedSqlServerName = useExistingSql ? existingSqlServerName : sqlServerName
var resolvedSqlDatabaseName = useExistingSql ? existingSqlDatabaseName : sqlDatabaseName

// Log Analytics is required for ACA diagnostics and troubleshooting.
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${baseName}-law'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    features: {
      searchVersion: 1
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

// Container Apps environment is the shared compute boundary for API, FE, and worker.
resource acaEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${baseName}-acae'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// ACR is included so azd deploy can push images for Container Apps workloads.
resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: toLower(replace('${baseName}acr', '-', ''))
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

// User-assigned managed identity for Container Apps to pull images from ACR.
resource containerAppsManagedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: containerAppsManagedIdentityName
  location: location
  tags: tags
}

// Grant AcrPull role to the managed identity on the container registry.
resource acrPullRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistry.id, containerAppsManagedIdentity.id, 'AcrPull')
  scope: containerRegistry
  properties: {
    principalId: containerAppsManagedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: acrPullRoleDefinitionId
  }
}

// Azure SQL logical server for backend transactional data.
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = if (!useExistingSql) {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    publicNetworkAccess: 'Enabled'
    minimalTlsVersion: '1.2'
  }

  // Entra AD administrator (optional; requires sqlEntraAdminLogin and sqlEntraAdminObjectId)
  resource entraAdmin 'administrators@2023-08-01-preview' = if (!empty(sqlEntraAdminLogin)) {
    name: 'ActiveDirectory'
    properties: {
      administratorType: 'ActiveDirectory'
      login: sqlEntraAdminLogin
      sid: sqlEntraAdminObjectId
      tenantId: subscription().tenantId
    }
  }
}
// Allow Azure-hosted workloads (including ACA) to reach Azure SQL public endpoint.
resource sqlAllowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = if (!useExistingSql) {
  parent: sqlServer
  name: 'AllowAllAzureIPs'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = if (!useExistingSql) {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  tags: tags
  sku: {
    name: 'HS_Gen5_2'
    tier: 'Hyperscale'
    capacity: 2
    family: 'Gen5'
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 2147483648
    autoPauseDelay: 60
  }
}

// Backend API container app. azd deploy locates this by azd-service-name tag.
resource backendApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: backendAppName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${containerAppsManagedIdentity.id}': {}
    }
  }
  tags: union(tags, {
    'azd-service-name': 'backend'
  })
  properties: {
    managedEnvironmentId: acaEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
      registries: [
        {
          server: containerRegistry.properties.loginServer
          identity: containerAppsManagedIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'backend'
          image: defaultContainerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 2
      }
    }
  }
  dependsOn: [
    acrPullRoleAssignment
  ]
}

// Frontend web container app. azd deploy locates this by azd-service-name tag.
resource frontendApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: frontendAppName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${containerAppsManagedIdentity.id}': {}
    }
  }
  tags: union(tags, {
    'azd-service-name': 'frontend'
  })
  properties: {
    managedEnvironmentId: acaEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 3000
        transport: 'auto'
      }
      registries: [
        {
          server: containerRegistry.properties.loginServer
          identity: containerAppsManagedIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'frontend'
          image: defaultContainerImage
          env: [
            {
              name: 'BACKEND_API_BASE_URL'
              value: 'https://${backendApp.properties.configuration.ingress.fqdn}'
            }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 2
      }
    }
  }
  dependsOn: [
    acrPullRoleAssignment
  ]
}

// Worker container app. azd deploy locates this by azd-service-name tag.
resource workerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: workerAppName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${containerAppsManagedIdentity.id}': {}
    }
  }
  tags: union(tags, {
    'azd-service-name': 'worker'
  })
  properties: {
    managedEnvironmentId: acaEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [
        {
          server: containerRegistry.properties.loginServer
          identity: containerAppsManagedIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: defaultContainerImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 2
      }
    }
  }
  dependsOn: [
    acrPullRoleAssignment
  ]
}

output AZURE_LOCATION string = location
output AZURE_ENV_NAME string = environmentName
output AZURE_CONTAINER_APPS_ENVIRONMENT_ID string = acaEnvironment.id
output AZURE_CONTAINER_APPS_ENVIRONMENT_NAME string = acaEnvironment.name
output AZURE_CONTAINER_REGISTRY_NAME string = containerRegistry.name
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = containerRegistry.properties.loginServer
output BACKEND_CONTAINER_APP_NAME string = backendApp.name
output FRONTEND_CONTAINER_APP_NAME string = frontendApp.name
output WORKER_CONTAINER_APP_NAME string = workerApp.name
output AZURE_SQL_SERVER_NAME string = resolvedSqlServerName
output AZURE_SQL_DATABASE_NAME string = resolvedSqlDatabaseName
output AZURE_SQL_PROVISIONING_MODE string = sqlProvisioningMode
output FRONTEND_URL string = 'https://${frontendApp.properties.configuration.ingress.fqdn}'
output BACKEND_URL string = 'https://${backendApp.properties.configuration.ingress.fqdn}'
