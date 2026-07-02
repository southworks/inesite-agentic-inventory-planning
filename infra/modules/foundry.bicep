param location string
param resourceTags object
param foundryAccountName string
param baseName string
param modelDeploymentName string
param modelDeploymentSkuName string
param modelDeploymentCapacity int
param agentModelFormat string
param agentModelName string
param agentModelVersion string
@secure()
param applicationInsightsConnectionString string
param applicationInsightsId string

var foundryProjectName = '${baseName}-project'

var embedDeploymentName = 'text-embedding-3-small'
var embedModelFormat = 'OpenAI'
var embedModelName = 'text-embedding-3-small'
var embedModelVersion = '1'
var embeddingDimensions = '1536'

var embedDeploymentCapacity = 1000

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: foundryAccountName
  location: location
  tags: resourceTags
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    allowProjectManagement: true
    customSubDomainName: foundryAccountName
    publicNetworkAccess: 'Enabled'
  }
}

resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: foundryAccount
  name: foundryProjectName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {}
}

resource appInsightsConnection 'Microsoft.CognitiveServices/accounts/projects/connections@2025-06-01' = {
  parent: foundryProject
  name: 'appinsights'
  properties: {
    authType: 'ApiKey'
    category: 'AppInsights'
    target: applicationInsightsId
    useWorkspaceManagedIdentity: false
    isSharedToAll: true
    sharedUserList: []
    peRequirement: 'NotRequired'
    peStatus: 'NotApplicable'
    metadata: {
      ApiType: 'Azure'
      ResourceId: applicationInsightsId
    }
    credentials: {
      key: applicationInsightsConnectionString
    }
  }
}

resource modelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundryAccount
  name: modelDeploymentName
  sku: {
    name: modelDeploymentSkuName
    capacity: modelDeploymentCapacity
  }
  properties: {
    model: {
      format: agentModelFormat
      name: agentModelName
      version: agentModelVersion
    }
  }
  dependsOn: [
    foundryProject
  ]
}

resource embedModelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundryAccount
  name: embedDeploymentName
  sku: {
    name: 'GlobalStandard'
    capacity: embedDeploymentCapacity
  }
  properties: {
    model: {
      format: embedModelFormat
      name: embedModelName
      version: embedModelVersion
    }
  }
  dependsOn: [
    foundryProject
    modelDeployment
  ]
}

var foundryEndpointBase = 'https://${foundryAccount.properties.customSubDomainName}.services.ai.azure.com'
var embedEndpoint = '${foundryEndpointBase}/openai/deployments/${embedDeploymentName}'
var foundryProjectEndpoint = '${foundryEndpointBase}/api/projects/${foundryProject.name}'

output foundryAccountName string = foundryAccount.name
output foundryAccountId string = foundryAccount.id
output foundryAccountEndpoint string = foundryEndpointBase
output foundryProjectName string = foundryProject.name
output foundryProjectResourceId string = foundryProject.id
output foundryProjectPrincipalId string = foundryProject.identity.principalId
output foundryProjectEndpoint string = foundryProjectEndpoint
output embedEndpoint string = embedEndpoint
output modelDeploymentName string = modelDeploymentName
output embedDeploymentName string = embedDeploymentName
output embedModelName string = embedModelName
output embeddingDimensions string = embeddingDimensions
