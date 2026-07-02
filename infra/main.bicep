@description('Base name used for deployed resources.')
param baseName string = 'grokinventory'

@description('Foundry model deployment name used by all planning agents (Grok 4.3).')
param modelDeploymentName string = 'grok-4.3'

@description('SKU used by the Foundry model deployment for the agents. Use GlobalStandard for serverless deployments; use a provisioned SKU only if it is available for the model and region.')
param modelDeploymentSkuName string = 'GlobalStandard'

@minValue(1)
@description('Capacity units for the Foundry model deployment used by the agents. Increase this when agents fail with no_capacity during peak load.')
param modelDeploymentCapacity int = 100

@description('Foundry model provider format for the agent reasoning model.')
param agentModelFormat string = 'xAI'

@description('Grok 4.3 model name in the Foundry catalog.')
param agentModelName string = 'grok-4.3'

@description('Grok 4.3 model version in the Foundry catalog.')
param agentModelVersion string = '1'

@description('Foundry deployment name for the RAG embedding model (OpenAI text-embedding-3-small).')
param embedDeploymentName string = 'text-embedding-3-small'

@description('Foundry model provider format for the RAG embedding model.')
param embedModelFormat string = 'OpenAI'

@description('OpenAI embedding model name in the Foundry catalog.')
param embedModelName string = 'text-embedding-3-small'

@description('OpenAI embedding model version in the Foundry catalog.')
param embedModelVersion string = '1'

@description('Vector dimensions for Search indexes and embedding requests (1536 for text-embedding-3-small).')
param embeddingDimensions string = '1536'

@description('Agent memory store name for inventory planning workflow context.')
param memoryStoreName string = 'inventory-planning-agent-memory'

@description('Azure AI Search SKU for demo retrieval indexes.')
param searchSku string = 'standard'

@description('Enable Microsoft Fabric integration. When false, MCP uses bundled local dataset mode.')
param enableFabric bool = false

@description('Fabric workspace name. Required when enableFabric is true.')
param fabricWorkspaceName string = ''

@description('Fabric lakehouse name. Used only when enableFabric is true.')
param fabricLakehouseName string = 'InventoryPlanningLakehouse'

@description('UAMI resource ID from setup-fabric-provision-identity.ps1. Required when enableFabric is true.')
param fabricUamiResourceId string = ''

@description('When false, the lakehouse is still provisioned but no data is uploaded. Applies only when enableFabric is true.')
param enableFabricSeed bool = true

@description('Repository archive URL for the seed script to download infra/scripts/ and dataset-seed/.')
param fabricRepositoryArchiveUrl string = 'https://github.com/southworks/inesite-agentic-inventory-planning/archive/refs/heads/main.zip'

@secure()
@description('Optional GitHub PAT for private repos or higher rate limits.')
param fabricGithubToken string = ''

@description('Full container image URI for the API host.')
param apiContainerImage string = 'ghcr.io/southworks/inventoryplanning-api:demo'

@description('Full container image URI for the MCP host.')
param mcpContainerImage string = 'ghcr.io/southworks/inventoryplanning-mcp:demo'

@description('Full container image URI for the agent provisioning job.')
param provisioningContainerImage string = 'ghcr.io/southworks/inventoryplanning-provisioning:demo'

@description('Full container image URI for the frontend web app.')
param frontendContainerImage string = 'ghcr.io/southworks/inventoryplanning-web:demo'

var location = resourceGroup().location

var resourceTags = {
  project: 'inesite'
}

resource resourceGroupTags 'Microsoft.Resources/tags@2021-04-01' = {
  name: 'default'
  properties: {
    tags: resourceTags
  }
}

module naming 'modules/naming.bicep' = {
  name: 'naming'
  params: {
    baseName: baseName
  }
}

module dataServices 'modules/data-services.bicep' = {
  name: 'data-services'
  params: {
    location: location
    resourceTags: resourceTags
    searchServiceName: naming.outputs.searchServiceName
    searchSku: searchSku
  }
}

module platform 'modules/platform.bicep' = {
  name: 'platform'
  params: {
    location: location
    resourceTags: resourceTags
    logAnalyticsName: naming.outputs.logAnalyticsName
    applicationInsightsName: naming.outputs.applicationInsightsName
    containerAppsEnvironmentName: naming.outputs.containerAppsEnvironmentName
  }
}

module foundry 'modules/foundry.bicep' = {
  name: 'foundry'
  params: {
    location: location
    resourceTags: resourceTags
    foundryAccountName: naming.outputs.foundryAccountName
    baseName: baseName
    modelDeploymentName: modelDeploymentName
    modelDeploymentSkuName: modelDeploymentSkuName
    modelDeploymentCapacity: modelDeploymentCapacity
    agentModelFormat: agentModelFormat
    agentModelName: agentModelName
    agentModelVersion: agentModelVersion
    embedDeploymentName: embedDeploymentName
    embedModelFormat: embedModelFormat
    embedModelName: embedModelName
    embedModelVersion: embedModelVersion
    applicationInsightsResourceId: platform.outputs.applicationInsightsResourceId
    applicationInsightsConnectionString: platform.outputs.applicationInsightsConnectionString
  }
}

module security 'modules/security.bicep' = {
  name: 'security'
  params: {
    location: location
    resourceTags: resourceTags
    deploymentSuffix: naming.outputs.deploymentSuffix
    apiIdentityName: naming.outputs.apiIdentityName
    provisioningIdentityName: naming.outputs.provisioningIdentityName
    foundryAccountName: foundry.outputs.foundryAccountName
    foundryProjectName: foundry.outputs.foundryProjectName
    searchServiceName: dataServices.outputs.searchServiceName
    enableFabric: enableFabric
    fabricUamiResourceId: fabricUamiResourceId
    mcpIdentityName: naming.outputs.mcpIdentityName
    searchServicePrincipalId: dataServices.outputs.searchServicePrincipalId
  }
}

module fabricProvision 'modules/fabric-provision.bicep' = if (enableFabric) {
  name: 'fabric-provision'
  params: {
    location: location
    resourceTags: resourceTags
    deploymentSuffix: naming.outputs.deploymentSuffix
    fabricUamiResourceId: fabricUamiResourceId
    fabricWorkspaceName: fabricWorkspaceName
    fabricLakehouseName: fabricLakehouseName
  }
}

module containerApps 'modules/container-apps.bicep' = {
  name: 'container-apps'
  params: {
    location: location
    resourceTags: resourceTags
    containerAppsEnvironmentId: platform.outputs.containerAppsEnvironmentId
    apiAppName: naming.outputs.apiAppName
    mcpAppName: naming.outputs.mcpAppName
    frontendAppName: naming.outputs.frontendAppName
    apiContainerImage: apiContainerImage
    mcpContainerImage: mcpContainerImage
    frontendContainerImage: frontendContainerImage
    apiIdentityId: security.outputs.apiIdentityId
    apiIdentityClientId: security.outputs.apiIdentityClientId
    mcpIdentityId: security.outputs.mcpIdentityId
    mcpIdentityClientId: security.outputs.mcpIdentityClientId
    foundryProjectEndpoint: foundry.outputs.foundryProjectEndpoint
    searchServiceEndpoint: dataServices.outputs.searchServiceEndpoint
    embeddingDimensions: embeddingDimensions
    embedDeploymentName: foundry.outputs.embedDeploymentName
    embedModelName: foundry.outputs.embedModelName
    embedEndpoint: foundry.outputs.embedEndpoint
    enableFabric: enableFabric
    fabricWorkspaceName: fabricWorkspaceName
    fabricLakehouseName: fabricLakehouseName
  }
}

module containerJobs 'modules/container-jobs.bicep' = {
  name: 'container-jobs'
  params: {
    location: location
    resourceTags: resourceTags
    containerAppsEnvironmentId: platform.outputs.containerAppsEnvironmentId
    foundryIqBootstrapJobName: naming.outputs.foundryIqBootstrapJobName
    provisioningJobName: naming.outputs.provisioningJobName
    provisioningContainerImage: provisioningContainerImage
    mcpContainerImage: mcpContainerImage
    mcpIdentityId: security.outputs.mcpIdentityId
    mcpIdentityClientId: security.outputs.mcpIdentityClientId
    provisioningIdentityId: security.outputs.provisioningIdentityId
    provisioningIdentityClientId: security.outputs.provisioningIdentityClientId
    mcpUrl: containerApps.outputs.mcpUrl
    searchServiceEndpoint: dataServices.outputs.searchServiceEndpoint
    foundryResourceUri: foundry.outputs.foundryAccountEndpoint
    embedDeploymentName: foundry.outputs.embedDeploymentName
    embedModelName: foundry.outputs.embedModelName
    embeddingDimensions: embeddingDimensions
    foundryProjectEndpoint: foundry.outputs.foundryProjectEndpoint
    modelDeploymentName: foundry.outputs.modelDeploymentName
  }
}

module postDeployScripts 'modules/post-deploy-scripts.bicep' = {
  name: 'post-deploy-scripts'
  params: {
    location: location
    resourceTags: resourceTags
    deploymentSuffix: naming.outputs.deploymentSuffix
    deploymentScriptIdentityName: naming.outputs.deploymentScriptIdentityName
    foundryAccountName: foundry.outputs.foundryAccountName
    foundryProjectName: foundry.outputs.foundryProjectName
    provisioningJobName: containerJobs.outputs.provisioningJobName
    foundryIqBootstrapJobName: containerJobs.outputs.foundryIqBootstrapJobName
  }
}

module fabricSeed 'modules/fabric-seed.bicep' = if (enableFabric) {
  name: 'fabric-seed'
  params: {
    location: location
    resourceTags: resourceTags
    deploymentSuffix: naming.outputs.deploymentSuffix
    enableFabricSeed: enableFabricSeed
    fabricUamiResourceId: fabricUamiResourceId
    fabricWorkspaceId: fabricProvision!.outputs.workspaceId
    fabricWorkspaceName: fabricProvision!.outputs.workspaceName
    fabricLakehouseId: fabricProvision!.outputs.lakehouseId
    fabricLakehouseName: fabricProvision!.outputs.lakehouseName
    fabricRepositoryArchiveUrl: fabricRepositoryArchiveUrl
    fabricGithubToken: fabricGithubToken
  }
}

output foundryAccountName string = foundry.outputs.foundryAccountName
output foundryProjectName string = foundry.outputs.foundryProjectName
output foundryProjectEndpoint string = foundry.outputs.foundryProjectEndpoint
output foundryProjectResourceId string = foundry.outputs.foundryProjectResourceId
output modelDeploymentName string = foundry.outputs.modelDeploymentName
output embedDeploymentName string = foundry.outputs.embedDeploymentName
output embedModelName string = foundry.outputs.embedModelName
output memoryStoreName string = memoryStoreName
output searchServiceName string = dataServices.outputs.searchServiceName
output searchServiceEndpoint string = dataServices.outputs.searchServiceEndpoint
output fabricWorkspaceId string = enableFabric ? fabricProvision!.outputs.workspaceId : ''
output fabricWorkspaceName string = enableFabric ? fabricProvision!.outputs.workspaceName : ''
output fabricLakehouseId string = enableFabric ? fabricProvision!.outputs.lakehouseId : ''
output fabricLakehouseName string = enableFabric ? fabricProvision!.outputs.lakehouseName : ''
output fabricSqlServer string = enableFabric ? fabricProvision!.outputs.sqlServer : ''
output fabricSqlDatabase string = enableFabric ? fabricProvision!.outputs.sqlDatabase : ''
output containerAppsEnvironmentId string = platform.outputs.containerAppsEnvironmentId
output apiUrl string = containerApps.outputs.apiUrl
output mcpUrl string = containerApps.outputs.mcpUrl
output provisioningJobName string = containerJobs.outputs.provisioningJobName
output foundryIqBootstrapJobName string = containerJobs.outputs.foundryIqBootstrapJobName
output frontendUrl string = containerApps.outputs.frontendUrl
