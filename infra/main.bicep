targetScope = 'resourceGroup'

@description('Primary deployment location for all resources.')
param location string = resourceGroup().location

@description('Environment name for resource naming and tagging.')
param environmentName string = 'dev'

@description('Optional tags applied to all resources.')
param tags object = {}

var baseName = 'aih-${environmentName}-${uniqueString(resourceGroup().id)}'
var acrPullRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '7f951dda-4ed3-4680-a7ca-43fe172d538d'
)
var defaultContainerImage = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
var backendAppName = '${baseName}-backend'
var frontendAppName = '${baseName}-frontend'
var workerAppName = '${baseName}-worker'

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

// Backend API container app. azd deploy locates this by azd-service-name tag.
resource backendApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: backendAppName
  location: location
  identity: {
    type: 'SystemAssigned'
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
          identity: 'system'
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
}

// Frontend web container app. azd deploy locates this by azd-service-name tag.
resource frontendApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: frontendAppName
  location: location
  identity: {
    type: 'SystemAssigned'
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
          identity: 'system'
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
}

// Worker container app. azd deploy locates this by azd-service-name tag.
resource workerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: workerAppName
  location: location
  identity: {
    type: 'SystemAssigned'
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
          identity: 'system'
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
}

resource backendAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistry.id, backendApp.id, 'AcrPull')
  scope: containerRegistry
  properties: {
    principalId: backendApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: acrPullRoleDefinitionId
  }
}

resource frontendAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistry.id, frontendApp.id, 'AcrPull')
  scope: containerRegistry
  properties: {
    principalId: frontendApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: acrPullRoleDefinitionId
  }
}

resource workerAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistry.id, workerApp.id, 'AcrPull')
  scope: containerRegistry
  properties: {
    principalId: workerApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: acrPullRoleDefinitionId
  }
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
output FRONTEND_URL string = 'https://${frontendApp.properties.configuration.ingress.fqdn}'
output BACKEND_URL string = 'https://${backendApp.properties.configuration.ingress.fqdn}'
