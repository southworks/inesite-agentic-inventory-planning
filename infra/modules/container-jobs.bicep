param location string
param resourceTags object
param containerAppsEnvironmentId string
param policySeedJobName string
param provisioningJobName string
param mcpContainerImage string
param provisioningContainerImage string
param mcpIdentityId string
param provisioningIdentityId string
param provisioningIdentityClientId string
param mcpUrl string
param mcpContainerEnv array
param foundryProjectEndpoint string
param modelDeploymentName string

var policySeedContainerEnv = concat(mcpContainerEnv, [
  { name: 'AzureFoundryModels__MaxRetryAttempts', value: '10' }
  { name: 'AzureFoundryModels__MaxDelaySeconds', value: '60' }
])

resource policySeedJob 'Microsoft.App/jobs@2024-03-01' = {
  name: policySeedJobName
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
          name: 'policy-seed'
          image: mcpContainerImage
          command: [
            'dotnet'
            'GrokInventoryAndTrend.Mcp.dll'
            '--seed-policies'
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: policySeedContainerEnv
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

output policySeedJobName string = policySeedJob.name
output provisioningJobName string = provisioningJob.name
