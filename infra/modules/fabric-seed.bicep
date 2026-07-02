@description('Azure region for the deployment script resource.')
param location string

@description('Resource tags.')
param resourceTags object

@description('Unique deployment suffix from naming module.')
param deploymentSuffix string

@description('Whether to run the Fabric data seed.')
param enableFabricSeed bool

@description('Resource ID of the user-assigned managed identity used for Fabric access.')
param fabricUamiResourceId string

@description('Fabric workspace ID (from fabric-provision module).')
param fabricWorkspaceId string

@description('Fabric workspace name.')
param fabricWorkspaceName string

@description('Fabric lakehouse ID (from fabric-provision module).')
param fabricLakehouseId string

@description('Fabric lakehouse name.')
param fabricLakehouseName string

@description('Repository archive URL containing dataset-seed/ and infra/scripts/.')
param fabricRepositoryArchiveUrl string

@secure()
@description('Optional GitHub PAT for private repos or higher rate limits.')
param fabricGithubToken string

resource fabricUami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: last(split(fabricUamiResourceId, '/'))
}

resource runFabricSeed 'Microsoft.Resources/deploymentScripts@2023-08-01' = if (enableFabricSeed) {
  name: 'run-invplan-fabric-seed-${deploymentSuffix}'
  location: location
  tags: resourceTags
  kind: 'AzurePowerShell'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${fabricUami.id}': {}
    }
  }
  properties: {
    azPowerShellVersion: '11.0'
    retentionInterval: 'P1D'
    timeout: 'PT60M'
    cleanupPreference: 'OnSuccess'
    forceUpdateTag: deploymentSuffix
    scriptContent: loadTextContent('../scripts/seed-fabric-data.ps1')
    environmentVariables: [
      { name: 'AZURE_CLIENT_ID',         value: fabricUami.properties.clientId }
      { name: 'FABRIC_WORKSPACE_ID',     value: fabricWorkspaceId }
      { name: 'FABRIC_WORKSPACE_NAME',   value: fabricWorkspaceName }
      { name: 'FABRIC_LAKEHOUSE_ID',     value: fabricLakehouseId }
      { name: 'FABRIC_LAKEHOUSE_NAME',   value: fabricLakehouseName }
      { name: 'RESOURCE_GROUP_NAME',     value: resourceGroup().name }
      { name: 'REPOSITORY_ARCHIVE_URL',  value: fabricRepositoryArchiveUrl }
      { name: 'GITHUB_TOKEN',            secureValue: fabricGithubToken }
    ]
  }
}
