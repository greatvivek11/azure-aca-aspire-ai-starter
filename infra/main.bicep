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
param azureOpenAiApiKey string = ''

@description('Azure OpenAI model deployment ID used by backend.')
param azureOpenAiModelId string = ''

@description('Azure OpenAI endpoint URL used by backend.')
param azureOpenAiEndpoint string = ''

@allowed([
  'external'
  'provision'
])
@description('AI services strategy: external uses provided endpoint/key; provision creates an Azure OpenAI resource and uses its endpoint/key.')
param aiServicesProvisioningMode string = 'external'

@description('Azure OpenAI account name when aiServicesProvisioningMode is provision. Leave empty to auto-generate.')
param aiServicesAccountName string = ''

@description('Chat model deployment name. When provisioning, this deployment is created and used as AZURE_OPENAI_MODEL_ID.')
param openAiChatDeploymentName string = 'gpt-5-mini'

@description('Chat model catalog name for Azure OpenAI deployment.')
param openAiChatModelName string = 'gpt-5-mini'

@description('Chat model version for Azure OpenAI deployment.')
param openAiChatModelVersion string = ''

@description('Embeddings model deployment name provisioned for ingestion/RAG.')
param openAiEmbeddingDeploymentName string = 'text-embedding-3-small'

@description('Embeddings model catalog name.')
param openAiEmbeddingModelName string = 'text-embedding-3-small'

@description('Embeddings model version.')
param openAiEmbeddingModelVersion string = '1'

@description('Embedding vector dimensions for the embeddings deployment used by Azure AI Search vector field.')
param openAiEmbeddingDimensions int = 1536

@description('Storage account name override. Leave empty to auto-generate.')
param storageAccountName string = ''

@description('Blob container name used for uploaded documents.')
param documentsContainerName string = 'documents'

@description('Azure AI Search service name override. Leave empty to auto-generate.')
param searchServiceName string = ''

@description('Azure AI Search index name for document chunks.')
param searchIndexName string = 'documents-index'

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
var storageBlobDataContributorRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
)
var backendAppName = 'backend'
var frontendAppName = 'frontend'
var workerAppName = 'worker'
var containerAppsManagedIdentityName = '${baseName}-cai'
var sqlServerName = take('${baseName}-sql', 63)
var useExistingSql = toLower(sqlProvisioningMode) == 'existing'
var useManagedRegistry = toLower(containerRegistryMode) == 'managed'
var useExternalRegistry = !useManagedRegistry
var useProvisionedAiServices = toLower(aiServicesProvisioningMode) == 'provision'
var hasExternalRegistryCredentials = useExternalRegistry && !empty(externalRegistryUsername) && !empty(externalRegistryPassword)
var logAnalyticsEnabled = toLower(enableLogAnalytics) == 'true'
var aspireDashboardEnabled = toLower(enableAspireDashboard) == 'true'
var appLogsDestination = logAnalyticsEnabled
  ? 'log-analytics'
  : (aspireDashboardEnabled ? 'azure-monitor' : 'none')
var resolvedBackendMinReplicas = int(backendMinReplicas)
var resolvedFrontendMinReplicas = int(frontendMinReplicas)
var resolvedWorkerMinReplicas = int(workerMinReplicas)
var resolvedSqlServerName = useExistingSql ? existingSqlServerName : sqlServerName
var resolvedSqlDatabaseName = useExistingSql ? existingSqlDatabaseName : sqlDatabaseName
var generatedStorageAccountName = toLower(take('st${uniqueString(resourceGroup().id, environmentName)}', 24))
var resolvedStorageAccountName = empty(storageAccountName) ? generatedStorageAccountName : toLower(storageAccountName)
var generatedSearchServiceName = toLower(take(replace('${baseName}srch', '-', ''), 60))
var resolvedSearchServiceName = empty(searchServiceName) ? generatedSearchServiceName : toLower(searchServiceName)
var generatedAiServicesAccountName = toLower(take(replace('${baseName}aoai', '-', ''), 24))
var resolvedAiServicesAccountName = empty(aiServicesAccountName) ? generatedAiServicesAccountName : toLower(aiServicesAccountName)
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
    appLogsConfiguration: appLogsDestination == 'log-analytics'
      ? {
          destination: appLogsDestination
          logAnalyticsConfiguration: {
            customerId: logAnalytics!.properties.customerId
            sharedKey: logAnalytics!.listKeys().primarySharedKey
          }
        }
      : {
          destination: appLogsDestination
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

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: resolvedStorageAccountName
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource documentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: documentsContainerName
  properties: {
    publicAccess: 'None'
  }
}

resource blobDataContributorRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, containerAppsManagedIdentity.id, 'StorageBlobDataContributor')
  scope: storageAccount
  properties: {
    principalId: containerAppsManagedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: storageBlobDataContributorRoleDefinitionId
  }
}

resource aiSearch 'Microsoft.Search/searchServices@2023-11-01' = {
  name: resolvedSearchServiceName
  location: location
  tags: tags
  sku: {
    name: 'basic'
  }
  properties: {
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'default'
    publicNetworkAccess: 'enabled'
  }
}

resource aiServicesAccount 'Microsoft.CognitiveServices/accounts@2024-10-01' = if (useProvisionedAiServices) {
  name: resolvedAiServicesAccountName
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: resolvedAiServicesAccountName
    publicNetworkAccess: 'Enabled'
  }
}

resource chatModelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = if (useProvisionedAiServices) {
  parent: aiServicesAccount
  name: openAiChatDeploymentName
  sku: {
    name: 'GlobalStandard'
    capacity: 1
  }
  properties: {
    model: empty(openAiChatModelVersion)
      ? {
          format: 'OpenAI'
          name: openAiChatModelName
        }
      : {
          format: 'OpenAI'
          name: openAiChatModelName
          version: openAiChatModelVersion
        }
  }
}

resource embeddingModelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = if (useProvisionedAiServices) {
  parent: aiServicesAccount
  name: openAiEmbeddingDeploymentName
  sku: {
    name: 'GlobalStandard'
    capacity: 1
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: openAiEmbeddingModelName
      version: openAiEmbeddingModelVersion
    }
  }
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
var resolvedOpenAiApiKey = useProvisionedAiServices ? aiServicesAccount!.listKeys().key1 : azureOpenAiApiKey
var resolvedOpenAiEndpoint = useProvisionedAiServices ? aiServicesAccount!.properties.endpoint : azureOpenAiEndpoint
var resolvedOpenAiModelId = useProvisionedAiServices ? openAiChatDeploymentName : azureOpenAiModelId
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
var searchEndpoint = 'https://${aiSearch.name}.search.windows.net'
var searchApiKey = aiSearch.listAdminKeys().primaryKey
var ragRuntimeSecrets = [
  {
    name: 'azure-openai-api-key'
    value: resolvedOpenAiApiKey
  }
  {
    name: 'storage-connection-string'
    value: storageConnectionString
  }
  {
    name: 'search-api-key'
    value: searchApiKey
  }
]
var backendRegistrySecrets = concat(ragRuntimeSecrets, [])
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
              value: resolvedOpenAiModelId
            }
            {
              name: 'AZURE_OPENAI_ENDPOINT'
              value: resolvedOpenAiEndpoint
            }
            {
              name: 'AZURE_OPENAI_EMBEDDING_MODEL_ID'
              value: openAiEmbeddingDeploymentName
            }
            {
              name: 'AZURE_OPENAI_EMBEDDING_DIMENSIONS'
              value: string(openAiEmbeddingDimensions)
            }
            {
              name: 'AZURE_STORAGE_ACCOUNT_NAME'
              value: storageAccount.name
            }
            {
              name: 'AZURE_STORAGE_CONTAINER_NAME'
              value: documentsContainer.name
            }
            {
              name: 'AZURE_STORAGE_CONNECTION_STRING'
              secretRef: 'storage-connection-string'
            }
            {
              name: 'AZURE_SEARCH_ENDPOINT'
              value: searchEndpoint
            }
            {
              name: 'AZURE_SEARCH_INDEX_NAME'
              value: searchIndexName
            }
            {
              name: 'AZURE_SEARCH_API_KEY'
              secretRef: 'search-api-key'
            }
            {
              name: 'WORKER_DAPR_BASE_URL'
              value: 'http://localhost:3500/v1.0/invoke/${workerAppId}/method'
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
      secrets: concat(ragRuntimeSecrets, sharedRegistrySecrets)
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
          env: concat(workerAppInsightsEnv, [
            {
              name: 'AZURE_OPENAI_API_KEY'
              secretRef: 'azure-openai-api-key'
            }
            {
              name: 'AZURE_OPENAI_MODEL_ID'
              value: resolvedOpenAiModelId
            }
            {
              name: 'AZURE_OPENAI_ENDPOINT'
              value: resolvedOpenAiEndpoint
            }
            {
              name: 'AZURE_OPENAI_EMBEDDING_MODEL_ID'
              value: openAiEmbeddingDeploymentName
            }
            {
              name: 'AZURE_OPENAI_EMBEDDING_DIMENSIONS'
              value: string(openAiEmbeddingDimensions)
            }
            {
              name: 'AZURE_STORAGE_ACCOUNT_NAME'
              value: storageAccount.name
            }
            {
              name: 'AZURE_STORAGE_CONTAINER_NAME'
              value: documentsContainer.name
            }
            {
              name: 'AZURE_STORAGE_CONNECTION_STRING'
              secretRef: 'storage-connection-string'
            }
            {
              name: 'AZURE_SEARCH_ENDPOINT'
              value: searchEndpoint
            }
            {
              name: 'AZURE_SEARCH_INDEX_NAME'
              value: searchIndexName
            }
            {
              name: 'AZURE_SEARCH_API_KEY'
              secretRef: 'search-api-key'
            }
            {
              name: 'SQL_SERVER'
              value: '${resolvedSqlServerName}${environment().suffixes.sqlServerHostname}'
            }
            {
              name: 'SQL_DATABASE'
              value: resolvedSqlDatabaseName
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: containerAppsManagedIdentity.properties.clientId
            }
          ])
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
output AZURE_STORAGE_ACCOUNT_NAME string = storageAccount.name
output AZURE_STORAGE_DOCUMENTS_CONTAINER string = documentsContainer.name
output AZURE_SEARCH_SERVICE_NAME string = aiSearch.name
output AZURE_SEARCH_ENDPOINT string = searchEndpoint
output AZURE_SEARCH_INDEX_NAME string = searchIndexName
output AZURE_AI_SERVICES_PROVISIONING_MODE string = aiServicesProvisioningMode
output AZURE_OPENAI_EFFECTIVE_ENDPOINT string = resolvedOpenAiEndpoint
output AZURE_OPENAI_EFFECTIVE_MODEL_ID string = resolvedOpenAiModelId
output AZURE_OPENAI_EMBEDDING_MODEL_ID string = openAiEmbeddingDeploymentName
output AZURE_OPENAI_EMBEDDING_DIMENSIONS int = openAiEmbeddingDimensions
output AZURE_AI_SERVICES_ACCOUNT_NAME string = useProvisionedAiServices ? aiServicesAccount!.name : ''
output CONTAINER_REGISTRY_MODE string = containerRegistryMode
output ENABLE_LOG_ANALYTICS string = enableLogAnalytics
output ENABLE_ASPIRE_DASHBOARD string = enableAspireDashboard
output FRONTEND_URL string = 'https://${frontendApp.properties.configuration.ingress.fqdn}'
output BACKEND_URL string = 'https://${backendApp.properties.configuration.ingress.fqdn}'
