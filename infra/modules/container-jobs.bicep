param location string
param resourceTags object
param containerAppsEnvironmentId string
param foundryIqBootstrapJobName string
param provisioningJobName string
param mcpContainerImage string
param provisioningContainerImage string
param mcpIdentityId string
param provisioningIdentityId string
param provisioningIdentityClientId string
param mcpIdentityClientId string
param mcpUrl string
param searchServiceEndpoint string
@secure()
param storageConnectionString string
param foundryResourceUri string
param embedDeploymentName string
param embedModelName string
param foundryProjectEndpoint string
param modelDeploymentName string

var foundryIqBootstrapContainerEnv = [
  { name: 'FoundryIq__SearchEndpoint', value: searchServiceEndpoint }
  { name: 'FoundryIqBootstrap__SearchEndpoint', value: searchServiceEndpoint }
  { name: 'FoundryIqBootstrap__StorageConnectionString', value: storageConnectionString }
  { name: 'FoundryIqBootstrap__PolicyContainerName', value: 'policy-knowledge' }
  { name: 'FoundryIqBootstrap__PolicyKnowledgeSourceName', value: 'inventory-policy-knowledge-ks' }
  { name: 'FoundryIqBootstrap__PolicyKnowledgeBaseName', value: 'inventory-policy-knowledge-kb' }
  { name: 'FoundryIqBootstrap__FoundryResourceUri', value: foundryResourceUri }
  { name: 'FoundryIqBootstrap__EmbedDeploymentName', value: embedDeploymentName }
  { name: 'FoundryIqBootstrap__EmbedModelName', value: embedModelName }
  { name: 'FoundryIqBootstrap__PolicyFilePath', value: '/app/dataset-seed/policy_rag.txt' }
  { name: 'FoundryIqBootstrap__IndexerPollAttempts', value: '120' }
  { name: 'FoundryIqBootstrap__IndexerPollDelaySeconds', value: '20' }
  { name: 'AZURE_CLIENT_ID', value: mcpIdentityClientId }
]

resource foundryIqBootstrapJob 'Microsoft.App/jobs@2024-03-01' = {
  name: foundryIqBootstrapJobName
  location: location
  tags: resourceTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${mcpIdentityId}': {}
    }
  }
  properties: {
    environmentId: containerAppsEnvironmentId
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 3600
      replicaRetryLimit: 0
      manualTriggerConfig: {
        replicaCompletionCount: 1
        parallelism: 1
      }
    }
    template: {
      containers: [
        {
          name: 'foundry-iq-bootstrap'
          image: mcpContainerImage
          command: [
            'dotnet'
            'GrokInventoryAndTrend.Mcp.dll'
            '--bootstrap-foundry-iq'
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: foundryIqBootstrapContainerEnv
        }
      ]
    }
  }
}

resource provisioningJob 'Microsoft.App/jobs@2024-03-01' = {
  name: provisioningJobName
  location: location
  tags: resourceTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${provisioningIdentityId}': {}
    }
  }
  properties: {
    environmentId: containerAppsEnvironmentId
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 1800
      replicaRetryLimit: 0
      manualTriggerConfig: {
        replicaCompletionCount: 1
        parallelism: 1
      }
    }
    template: {
      containers: [
        {
          name: 'agent-provisioning'
          image: provisioningContainerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'AZURE_FOUNDRY_PROJECT_ENDPOINT', value: foundryProjectEndpoint }
            { name: 'FOUNDRY_PROJECT_ENDPOINT', value: foundryProjectEndpoint }
            { name: 'ProjectEndpoint', value: foundryProjectEndpoint }
            { name: 'AZURE_AI_MODEL_DEPLOYMENT_NAME', value: modelDeploymentName }
            { name: 'ModelDeploymentName', value: modelDeploymentName }
            { name: 'MCP_BASE_URL', value: mcpUrl }
            { name: 'AZURE_CLIENT_ID', value: provisioningIdentityClientId }
          ]
        }
      ]
    }
  }
}

output foundryIqBootstrapJobName string = foundryIqBootstrapJob.name
output provisioningJobName string = provisioningJob.name
