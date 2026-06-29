param location string
param resourceTags object
param deploymentSuffix string
param fabricUamiResourceId string
param fabricWorkspaceName string
param fabricLakehouseName string

resource fabricUami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: last(split(fabricUamiResourceId, '/'))
}

resource runFabricProvision 'Microsoft.Resources/deploymentScripts@2023-08-01' = {
  name: 'run-invplan-fabric-provision-${deploymentSuffix}'
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
    timeout: 'PT15M'
    cleanupPreference: 'OnSuccess'
    forceUpdateTag: deploymentSuffix
    scriptContent: loadTextContent('../scripts/provision-fabric-lakehouse.ps1')
    environmentVariables: [
      { name: 'AZURE_CLIENT_ID',       value: fabricUami.properties.clientId }
      { name: 'FABRIC_WORKSPACE_NAME', value: fabricWorkspaceName }
      { name: 'FABRIC_LAKEHOUSE_NAME', value: fabricLakehouseName }
    ]
  }
}

output workspaceId string = runFabricProvision.properties.outputs.workspaceId
output workspaceName string = runFabricProvision.properties.outputs.workspaceName
output lakehouseId string = runFabricProvision.properties.outputs.lakehouseId
output lakehouseName string = runFabricProvision.properties.outputs.lakehouseName
output sqlServer string = runFabricProvision.properties.outputs.sqlServer
output sqlDatabase string = runFabricProvision.properties.outputs.sqlDatabase
