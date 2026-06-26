param location string
param resourceTags object
param containerAppsEnvironmentId string
param deployFrontend bool
param apiAppName string
param mcpAppName string
param frontendAppName string
param apiContainerImage string
param mcpContainerImage string
param frontendContainerImage string
param apiIdentityId string
param apiIdentityClientId string
param mcpIdentityId string
param mcpIdentityClientId string
param foundryProjectEndpoint string
param searchServiceEndpoint string
// TODO: enable when Document Intelligence is needed
// param documentIntelligenceEndpoint string
param embeddingDimensions string
param embedDeploymentName string
param embedModelName string
param rerankDeploymentName string
param rerankModelName string
param embedEndpoint string
param rerankEndpoint string

var mcpFoundryModelEnv = [
  { name: 'AzureFoundryModels__EmbedDeploymentName', value: embedDeploymentName }
  { name: 'AzureFoundryModels__RerankDeploymentName', value: rerankDeploymentName }
  { name: 'AzureFoundryModels__EmbedModelName', value: embedModelName }
  { name: 'AzureFoundryModels__RerankModelName', value: rerankModelName }
  { name: 'AzureFoundryModels__EmbedEndpoint', value: embedEndpoint }
  { name: 'AzureFoundryModels__RerankEndpoint', value: rerankEndpoint }
  { name: 'AzureFoundryModels__EmbeddingDimensions', value: embeddingDimensions }
  { name: 'AzureFoundryModels__EmbeddingBatchSize', value: '16' }
  { name: 'AzureFoundryModels__MaxConcurrentEmbeddingRequests', value: '1' }
  { name: 'AzureFoundryModels__MaxConcurrentRerankRequests', value: '2' }
]

var mcpContainerEnv = concat([
  { name: 'AzureSearch__Endpoint', value: searchServiceEndpoint }
  { name: 'AzureSearch__EvidenceIndexName', value: 'inventory-signal-evidence' }
  { name: 'AzureSearch__PromotionsIndexName', value: 'promotions-price-knowledge' }
  { name: 'AzureSearch__VectorDimensions', value: embeddingDimensions }
  { name: 'Dataset__RootPath', value: '/app/dataset-seed' }
  { name: 'Dataset__PromotionsFilePath', value: '/app/dataset-seed/promotions-price-rag/promotions_price_calendar.txt' }
  { name: 'AZURE_CLIENT_ID', value: mcpIdentityClientId }
], mcpFoundryModelEnv)

resource mcpApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: mcpAppName
  location: location
  tags: resourceTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${mcpIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironmentId
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
    }
    template: {
      containers: [
        {
          name: 'mcp'
          image: mcpContainerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: concat(mcpContainerEnv, [
            { name: 'McpStartup__EnsureSearchIndexesOnStartup', value: 'true' }
            { name: 'McpStartup__SeedPromotionsOnStartup', value: 'false' }
          ])
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 15
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: apiAppName
  location: location
  tags: resourceTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${apiIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironmentId
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiContainerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'AZURE_FOUNDRY_PROJECT_ENDPOINT', value: foundryProjectEndpoint }
            { name: 'AzureSearch__Endpoint', value: searchServiceEndpoint }
            { name: 'AzureSearch__EvidenceIndexName', value: 'inventory-signal-evidence' }
            { name: 'AzureSearch__PromotionsIndexName', value: 'promotions-price-knowledge' }
            { name: 'AzureSearch__VectorDimensions', value: embeddingDimensions }
            { name: 'AzureFoundryModels__EmbedDeploymentName', value: embedDeploymentName }
            { name: 'AzureFoundryModels__RerankDeploymentName', value: rerankDeploymentName }
            { name: 'AzureFoundryModels__EmbedModelName', value: embedModelName }
            { name: 'AzureFoundryModels__RerankModelName', value: rerankModelName }
            { name: 'AzureFoundryModels__EmbedEndpoint', value: embedEndpoint }
            { name: 'AzureFoundryModels__RerankEndpoint', value: rerankEndpoint }
            { name: 'AzureFoundryModels__EmbeddingDimensions', value: embeddingDimensions }
            { name: 'AzureFoundryModels__EmbeddingBatchSize', value: '16' }
            { name: 'AzureFoundryModels__MaxConcurrentEmbeddingRequests', value: '1' }
            { name: 'AzureFoundryModels__MaxConcurrentRerankRequests', value: '2' }
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'AZURE_CLIENT_ID', value: apiIdentityClientId }
            // TODO: enable when Document Intelligence is needed
            // { name: 'DocumentExtraction__Endpoint', value: documentIntelligenceEndpoint }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 15
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

resource frontendApp 'Microsoft.App/containerApps@2024-03-01' = if (deployFrontend) {
  name: frontendAppName
  location: location
  tags: resourceTags
  properties: {
    managedEnvironmentId: containerAppsEnvironmentId
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
    }
    template: {
      containers: [
        {
          name: 'frontend'
          image: frontendContainerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ApiBaseUrl', value: 'https://${apiApp.properties.configuration.ingress.fqdn}/' }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 15
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

var mcpBaseUrl = 'https://${mcpApp.properties.configuration.ingress.fqdn}'

output apiUrl string = 'https://${apiApp.properties.configuration.ingress.fqdn}'
output frontendUrl string = deployFrontend ? 'https://${frontendApp!.properties.configuration.ingress.fqdn}' : ''
output mcpUrl string = mcpBaseUrl
output mcpFqdn string = mcpApp.properties.configuration.ingress.fqdn
output mcpContainerEnv array = mcpContainerEnv
