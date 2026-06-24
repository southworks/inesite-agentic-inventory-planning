@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Base name used for deployed resources.')
param baseName string = 'inesite'

@description('Foundry project name. Leave empty to default to {baseName}-project. Must be a plain string, not an ARM expression.')
param foundryProjectName string = ''

@description('Foundry model deployment name used by all planning agents (Grok 4.3).')
param modelDeploymentName string = 'grok-4.3'

@description('SKU used by the Foundry model deployment for the agents. Use GlobalStandard for serverless deployments; use a provisioned SKU only if it is available for the model and region.')
param modelDeploymentSkuName string = 'GlobalStandard'

@minValue(1)
@description('Capacity units for the Foundry model deployment used by the agents. Increase this when agents fail with no_capacity during peak load.')
param modelDeploymentCapacity int = 10

@description('Foundry model provider format for the agent reasoning model.')
param agentModelFormat string = 'xAI'

@description('Grok 4.3 model name in the Foundry catalog.')
param agentModelName string = 'grok-4.3'

@description('Grok 4.3 model version in the Foundry catalog.')
param agentModelVersion string = '1'

@description('Foundry deployment name for the RAG embedding model.')
param embedDeploymentName string = 'rag-embed'

@description('Foundry model provider format for the RAG embedding model. Final model selection is TBD per workflow-summary.')
param embedModelFormat string = 'Cohere'

@description('RAG embedding model name in the Foundry catalog. Interim default until a final model is selected.')
param embedModelName string = 'embed-v-4-0'

@description('RAG embedding model version in the Foundry catalog.')
param embedModelVersion string = '1'

@minValue(1)
@description('Capacity units for the RAG embed deployment. Increase this for faster signal evidence indexing and fewer throttling failures.')
param embedDeploymentCapacity int = 10

@description('Foundry deployment name for the RAG rerank model.')
param rerankDeploymentName string = 'rag-rerank'

@description('Foundry model provider format for the RAG rerank model. Final model selection is TBD per workflow-summary.')
param rerankModelFormat string = 'Cohere'

@description('RAG rerank model name in the Foundry catalog. Interim default until a final model is selected.')
param rerankModelName string = 'Cohere-rerank-v4.0-pro'

@description('RAG rerank model version in the Foundry catalog.')
param rerankModelVersion string = '1'

@minValue(1)
@description('Capacity units for the RAG rerank deployment. Increase this for faster retrieval reranking and fewer throttling failures.')
param rerankDeploymentCapacity int = 5

@description('Blob container for planning uploads and data-entry corrections.')
param documentsContainerName string = 'planning-documents'

@description('Agent memory store name for inventory planning workflow context.')
param memoryStoreName string = 'inventory-planning-agent-memory'

@description('Azure AI Search SKU for demo retrieval indexes.')
param searchSku string = 'basic'

@description('Full container image URI for the API host.')
param apiContainerImage string = 'ghcr.io/southworks/inesite-api:demo'

@description('Full container image URI for the MCP host.')
param mcpContainerImage string = 'ghcr.io/southworks/inesite-mcp:demo'

@description('Full container image URI for the frontend host.')
param frontendContainerImage string = 'ghcr.io/southworks/inesite-web:demo'

@description('Full container image URI for the agent provisioning job.')
param provisioningContainerImage string = 'ghcr.io/southworks/inesite-provisioning:demo'

@description('Optional suffix for retry deployments. Set when redeploying after a partial failure left names reserved.')
param nameSuffix string = ''

var resolvedFoundryProjectName = empty(foundryProjectName) ? '${baseName}-project' : foundryProjectName

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
    nameSuffix: nameSuffix
  }
}

module dataServices 'modules/data-services.bicep' = {
  name: 'data-services'
  params: {
    location: location
    resourceTags: resourceTags
    storageAccountName: naming.outputs.storageAccountName
    documentsContainerName: documentsContainerName
    searchServiceName: naming.outputs.searchServiceName
    searchSku: searchSku
    documentIntelligenceAccountName: naming.outputs.documentIntelligenceAccountName
  }
}

module foundry 'modules/foundry.bicep' = {
  name: 'foundry'
  params: {
    location: location
    resourceTags: resourceTags
    foundryAccountName: naming.outputs.foundryAccountName
    resolvedFoundryProjectName: resolvedFoundryProjectName
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
    embedDeploymentCapacity: embedDeploymentCapacity
    rerankDeploymentName: rerankDeploymentName
    rerankModelFormat: rerankModelFormat
    rerankModelName: rerankModelName
    rerankModelVersion: rerankModelVersion
    rerankDeploymentCapacity: rerankDeploymentCapacity
  }
}

module platform 'modules/platform.bicep' = {
  name: 'platform'
  params: {
    location: location
    resourceTags: resourceTags
    logAnalyticsName: naming.outputs.logAnalyticsName
    containerAppsEnvironmentName: naming.outputs.containerAppsEnvironmentName
  }
}

module security 'modules/security.bicep' = {
  name: 'security'
  params: {
    location: location
    resourceTags: resourceTags
    nameSuffix: nameSuffix
    apiIdentityName: naming.outputs.apiIdentityName
    mcpIdentityName: naming.outputs.mcpIdentityName
    provisioningIdentityName: naming.outputs.provisioningIdentityName
    storageAccountName: dataServices.outputs.storageAccountName
    foundryAccountName: foundry.outputs.foundryAccountName
    foundryProjectName: foundry.outputs.foundryProjectName
    searchServiceName: dataServices.outputs.searchServiceName
    documentIntelligenceAccountName: dataServices.outputs.documentIntelligenceAccountName
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
    blobServiceUri: dataServices.outputs.blobServiceUri
    documentsContainerName: dataServices.outputs.documentsContainerName
    searchServiceEndpoint: dataServices.outputs.searchServiceEndpoint
    documentIntelligenceEndpoint: dataServices.outputs.documentIntelligenceEndpoint
    embedDeploymentName: foundry.outputs.embedDeploymentName
    embedModelName: foundry.outputs.embedModelName
    rerankDeploymentName: foundry.outputs.rerankDeploymentName
    rerankModelName: foundry.outputs.rerankModelName
    embedEndpoint: foundry.outputs.embedEndpoint
    rerankEndpoint: foundry.outputs.rerankEndpoint
  }
}

module containerJobs 'modules/container-jobs.bicep' = {
  name: 'container-jobs'
  params: {
    location: location
    resourceTags: resourceTags
    containerAppsEnvironmentId: platform.outputs.containerAppsEnvironmentId
    promotionsSeedJobName: naming.outputs.promotionsSeedJobName
    provisioningJobName: naming.outputs.provisioningJobName
    mcpContainerImage: mcpContainerImage
    provisioningContainerImage: provisioningContainerImage
    mcpIdentityId: security.outputs.mcpIdentityId
    provisioningIdentityId: security.outputs.provisioningIdentityId
    provisioningIdentityClientId: security.outputs.provisioningIdentityClientId
    mcpUrl: containerApps.outputs.mcpUrl
    mcpContainerEnv: containerApps.outputs.mcpContainerEnv
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
    nameSuffix: nameSuffix
    deploymentScriptIdentityName: naming.outputs.deploymentScriptIdentityName
    foundryAccountName: foundry.outputs.foundryAccountName
    foundryProjectName: foundry.outputs.foundryProjectName
    promotionsSeedJobName: containerJobs.outputs.promotionsSeedJobName
    provisioningJobName: containerJobs.outputs.provisioningJobName
  }
}

output storageAccountName string = dataServices.outputs.storageAccountName
output blobServiceUri string = dataServices.outputs.blobServiceUri
output documentsContainerName string = dataServices.outputs.documentsContainerName
output foundryAccountName string = foundry.outputs.foundryAccountName
output foundryProjectName string = foundry.outputs.foundryProjectName
output foundryProjectEndpoint string = foundry.outputs.foundryProjectEndpoint
output foundryProjectResourceId string = foundry.outputs.foundryProjectResourceId
output modelDeploymentName string = foundry.outputs.modelDeploymentName
output embedDeploymentName string = foundry.outputs.embedDeploymentName
output embedModelName string = foundry.outputs.embedModelName
output rerankDeploymentName string = foundry.outputs.rerankDeploymentName
output rerankModelName string = foundry.outputs.rerankModelName
output memoryStoreName string = memoryStoreName
output searchServiceName string = dataServices.outputs.searchServiceName
output searchServiceEndpoint string = dataServices.outputs.searchServiceEndpoint
output documentIntelligenceAccountName string = dataServices.outputs.documentIntelligenceAccountName
output documentIntelligenceEndpoint string = dataServices.outputs.documentIntelligenceEndpoint
output apiUrl string = containerApps.outputs.apiUrl
output frontendUrl string = containerApps.outputs.frontendUrl
output mcpUrl string = containerApps.outputs.mcpUrl
output promotionsSeedJobName string = containerJobs.outputs.promotionsSeedJobName
output provisioningJobName string = containerJobs.outputs.provisioningJobName
