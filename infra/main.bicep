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

@secure()
@description('Azure OpenAI API key for backend AI calls.')
param azureOpenAiApiKey string

@description('Azure OpenAI model deployment ID used by backend.')
param azureOpenAiModelId string

@description('Azure OpenAI endpoint URL used by backend.')
param azureOpenAiEndpoint string

@allowed([
  'managed'
  'external'
])
@description('Container image registry mode. managed provisions Azure Container Registry; external uses public/authenticated external images such as GHCR.')
param containerRegistryMode string = 'external'

@description('External registry server used when containerRegistryMode is external.')
param externalRegistryServer string = 'ghcr.io'

@description('External registry username used when containerRegistryMode is external and the registry requires authentication.')
param externalRegistryUsername string = ''

@secure()
@description('External registry password or token used when containerRegistryMode is external and the registry requires authentication.')
param externalRegistryPassword string = ''

@description('Enable Log Analytics and workspace-based Application Insights for the Container Apps environment.')
param enableLogAnalytics string = 'false'

@description('Enable Aspire Dashboard in the Container Apps environment.')
param enableAspireDashboard string = 'true'

@description('Minimum replicas for the backend container app.')
param backendMinReplicas string = '0'

@description('Minimum replicas for the frontend container app.')
param frontendMinReplicas string = '0'

@description('Minimum replicas for the worker container app.')
param workerMinReplicas string = '0'

@description('Container image for the backend app.')
param backendImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Container image for the frontend app.')
param frontendImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Container image for the worker app.')
param workerImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

var baseName = 'aih-${environmentName}-${uniqueString(resourceGroup().id)}'
var acrPullRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '7f951dda-4ed3-4680-a7ca-43fe172d538d'
)
var backendAppName = 'backend'
var frontendAppName = 'frontend'
var workerAppName = 'worker'
var containerAppsManagedIdentityName = '${baseName}-cai'
var sqlServerName = take('${baseName}-sql', 63)
var useExistingSql = toLower(sqlProvisioningMode) == 'existing'
var useManagedRegistry = toLower(containerRegistryMode) == 'managed'
var useExternalRegistry = !useManagedRegistry
var hasExternalRegistryCredentials = useExternalRegistry && !empty(externalRegistryUsername) && !empty(externalRegistryPassword)
var logAnalyticsEnabled = toLower(enableLogAnalytics) == 'true'
var aspireDashboardEnabled = toLower(enableAspireDashboard) == 'true'
var resolvedBackendMinReplicas = int(backendMinReplicas)
var resolvedFrontendMinReplicas = int(frontendMinReplicas)
var resolvedWorkerMinReplicas = int(workerMinReplicas)
var resolvedSqlServerName = useExistingSql ? existingSqlServerName : sqlServerName
var resolvedSqlDatabaseName = useExistingSql ? existingSqlDatabaseName : sqlDatabaseName
var backendAppId = 'aihub-backend'
var frontendAppId = 'aihub-frontend'
var workerAppId = 'aihub-worker'

// Log Analytics is required for ACA diagnostics and troubleshooting.
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = if (logAnalyticsEnabled) {
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

// Workspace-based Application Insights receives logs, traces, and custom metrics from all workloads.
resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = if (logAnalyticsEnabled) {
  name: '${baseName}-appi'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
  }
}

// Container Apps environment is the shared compute boundary for API, FE, and worker.
resource acaEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${baseName}-acae'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: logAnalyticsEnabled
      ? {
          destination: 'log-analytics'
          logAnalyticsConfiguration: {
            customerId: logAnalytics!.properties.customerId
            sharedKey: logAnalytics!.listKeys().primarySharedKey
          }
        }
      : {
          destination: 'none'
        }
    daprAIConnectionString: logAnalyticsEnabled ? applicationInsights!.properties.ConnectionString : null
  }
}

resource aspireDashboard 'Microsoft.App/managedEnvironments/dotNetComponents@2025-10-02-preview' = if (aspireDashboardEnabled) {
  parent: acaEnvironment
  name: 'aspire-dashboard'
  properties: {
    componentType: 'AspireDashboard'
    configurations: []
    serviceBinds: []
  }
}

resource containerAppsManagedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: containerAppsManagedIdentityName
  location: location
  tags: tags
}

// ACR is optional. External/public registries avoid fixed monthly charges for low-cost templates.
resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' = if (useManagedRegistry) {
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

// Grant AcrPull role only when the deployment provisions a managed registry.
resource acrPullRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (useManagedRegistry) {
  name: guid(containerRegistry.id, containerAppsManagedIdentity.id, 'AcrPull')
  scope: containerRegistry
  properties: {
    principalId: containerAppsManagedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: acrPullRoleDefinitionId
  }
}

var backendAppInsightsEnv = logAnalyticsEnabled
  ? [
      {
        name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
        value: applicationInsights!.properties.ConnectionString
      }
    ]
  : []
var frontendAppInsightsEnv = logAnalyticsEnabled
  ? [
      {
        name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
        value: applicationInsights!.properties.ConnectionString
      }
    ]
  : []
var workerAppInsightsEnv = logAnalyticsEnabled
  ? [
      {
        name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
        value: applicationInsights!.properties.ConnectionString
      }
    ]
  : []
var backendRegistrySecrets = [
  {
    name: 'azure-openai-api-key'
    value: azureOpenAiApiKey
  }
]
var sharedRegistrySecrets = hasExternalRegistryCredentials
  ? [
      {
        name: 'registry-password'
        value: externalRegistryPassword
      }
    ]
  : []
var backendRegistryConfiguration = useManagedRegistry
  ? [
      {
        server: containerRegistry!.properties.loginServer
        identity: containerAppsManagedIdentity.id
      }
    ]
  : (hasExternalRegistryCredentials
      ? [
          {
            server: externalRegistryServer
            username: externalRegistryUsername
            passwordSecretRef: 'registry-password'
          }
        ]
      : [])
var externalRegistryConfiguration = useManagedRegistry
  ? [
      {
        server: containerRegistry!.properties.loginServer
        identity: containerAppsManagedIdentity.id
      }
    ]
  : (hasExternalRegistryCredentials
      ? [
          {
            server: externalRegistryServer
            username: externalRegistryUsername
            passwordSecretRef: 'registry-password'
          }
        ]
      : [])

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
      dapr: {
        enabled: true
        appId: backendAppId
        appPort: 8080
        appProtocol: 'http'
      }
      secrets: concat(backendRegistrySecrets, sharedRegistrySecrets)
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
      registries: backendRegistryConfiguration
    }
    template: {
      containers: [
        {
          name: 'backend'
          image: backendImage
          env: concat(backendAppInsightsEnv, [
            {
              name: 'AZURE_OPENAI_API_KEY'
              secretRef: 'azure-openai-api-key'
            }
            {
              name: 'AZURE_OPENAI_MODEL_ID'
              value: azureOpenAiModelId
            }
            {
              name: 'AZURE_OPENAI_ENDPOINT'
              value: azureOpenAiEndpoint
            }
            // SQL server and database passed as plain env vars; auth uses Managed Identity.
            {
              name: 'SQL_SERVER'
              value: '${resolvedSqlServerName}${environment().suffixes.sqlServerHostname}'
            }
            {
              name: 'SQL_DATABASE'
              value: resolvedSqlDatabaseName
            }
            // UAMI client ID used by SqlClient for Active Directory Managed Identity auth.
            {
              name: 'AZURE_CLIENT_ID'
              value: containerAppsManagedIdentity.properties.clientId
            }
          ])
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        minReplicas: resolvedBackendMinReplicas
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
      dapr: {
        enabled: true
        appId: frontendAppId
        appPort: 3000
        appProtocol: 'http'
      }
      secrets: sharedRegistrySecrets
      ingress: {
        external: true
        targetPort: 3000
        transport: 'auto'
      }
      registries: externalRegistryConfiguration
    }
    template: {
      containers: [
        {
          name: 'frontend'
          image: frontendImage
          env: concat(frontendAppInsightsEnv, [
            {
              name: 'BACKEND_DAPR_BASE_URL'
              value: 'http://localhost:3500/v1.0/invoke/${backendAppId}/method'
            }
            {
              name: 'WORKER_DAPR_BASE_URL'
              value: 'http://localhost:3500/v1.0/invoke/${workerAppId}/method'
            }
          ])
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        minReplicas: resolvedFrontendMinReplicas
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
      dapr: {
        enabled: true
        appId: workerAppId
        appPort: 8081
        appProtocol: 'http'
      }
      secrets: sharedRegistrySecrets
      ingress: {
        external: false
        targetPort: 8081
        transport: 'auto'
      }
      registries: externalRegistryConfiguration
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: workerImage
          env: workerAppInsightsEnv
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
      scale: {
        minReplicas: resolvedWorkerMinReplicas
        maxReplicas: 2
      }
    }
  }
}

output AZURE_LOCATION string = location
output AZURE_ENV_NAME string = environmentName
output AZURE_CONTAINER_APPS_ENVIRONMENT_ID string = acaEnvironment.id
output AZURE_CONTAINER_APPS_ENVIRONMENT_NAME string = acaEnvironment.name
output AZURE_CONTAINER_REGISTRY_NAME string = useManagedRegistry ? containerRegistry!.name : ''
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = useManagedRegistry ? containerRegistry!.properties.loginServer : ''
output BACKEND_CONTAINER_APP_NAME string = backendApp.name
output UAMI_CLIENT_ID string = containerAppsManagedIdentity.properties.clientId
output UAMI_PRINCIPAL_ID string = containerAppsManagedIdentity.properties.principalId
output UAMI_NAME string = containerAppsManagedIdentity.name
output FRONTEND_CONTAINER_APP_NAME string = frontendApp.name
output WORKER_CONTAINER_APP_NAME string = workerApp.name
output AZURE_SQL_SERVER_NAME string = resolvedSqlServerName
output AZURE_SQL_DATABASE_NAME string = resolvedSqlDatabaseName
output AZURE_SQL_PROVISIONING_MODE string = sqlProvisioningMode
output CONTAINER_REGISTRY_MODE string = containerRegistryMode
output ENABLE_LOG_ANALYTICS string = enableLogAnalytics
output ENABLE_ASPIRE_DASHBOARD string = enableAspireDashboard
output FRONTEND_URL string = 'https://${frontendApp.properties.configuration.ingress.fqdn}'
output BACKEND_URL string = 'https://${backendApp.properties.configuration.ingress.fqdn}'
