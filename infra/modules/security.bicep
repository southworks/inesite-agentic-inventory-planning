param location string
param resourceTags object
param deploymentSuffix string
param apiIdentityName string
param provisioningIdentityName string
param foundryAccountName string
param foundryProjectName string
param searchServiceName string
param searchServicePrincipalId string
param fabricUamiResourceId string

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: foundryAccountName
}

resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' existing = {
  parent: foundryAccount
  name: foundryProjectName
}

resource searchService 'Microsoft.Search/searchServices@2023-11-01' existing = {
  name: searchServiceName
}

resource fabricUami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: last(split(fabricUamiResourceId, '/'))
}

resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: apiIdentityName
  location: location
  tags: resourceTags
}

resource provisioningIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: provisioningIdentityName
  location: location
  tags: resourceTags
}

resource apiFoundryRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, apiIdentity.id, 'CognitiveServicesUser', deploymentSuffix)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'a97b65f3-24c7-4388-baec-2e87135dc908')
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [
    foundryProject
  ]
}

resource projectFoundryUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, foundryProject.id, 'FoundryUser', deploymentSuffix)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '53ca6127-db72-4b80-b1b0-d745d6d5456d')
    principalId: foundryProject.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource apiSearchContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(searchService.id, apiIdentity.id, 'SearchServiceContributor', deploymentSuffix)
  scope: searchService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7ca78c08-252a-4471-8644-bb5ff32d4ba0')
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource apiSearchDataRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(searchService.id, apiIdentity.id, 'SearchIndexDataContributor', deploymentSuffix)
  scope: searchService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8ebe5a00-799e-43f5-93ac-243d3dce84a7')
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource mcpSearchContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(searchService.id, fabricUami.id, 'SearchServiceContributor', deploymentSuffix)
  scope: searchService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7ca78c08-252a-4471-8644-bb5ff32d4ba0')
    principalId: fabricUami.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource mcpSearchDataRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(searchService.id, fabricUami.id, 'SearchIndexDataContributor', deploymentSuffix)
  scope: searchService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8ebe5a00-799e-43f5-93ac-243d3dce84a7')
    principalId: fabricUami.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource mcpFoundryRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, fabricUami.id, 'CognitiveServicesUser', deploymentSuffix)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'a97b65f3-24c7-4388-baec-2e87135dc908')
    principalId: fabricUami.properties.principalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [
    foundryProject
  ]
}

resource mcpFoundryUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, fabricUami.id, 'FoundryUser', deploymentSuffix)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '53ca6127-db72-4b80-b1b0-d745d6d5456d')
    principalId: fabricUami.properties.principalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [
    foundryProject
  ]
}

resource searchFoundryRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, searchServicePrincipalId, 'CognitiveServicesUser', deploymentSuffix)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'a97b65f3-24c7-4388-baec-2e87135dc908')
    principalId: searchServicePrincipalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [
    foundryProject
  ]
}

resource provisioningFoundryRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, provisioningIdentity.id, 'CognitiveServicesContributor', deploymentSuffix)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '25fbc0a9-bd7c-42a3-aa1a-3b75d497ee68')
    principalId: provisioningIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [
    foundryProject
  ]
}

resource provisioningFoundryDeveloperRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, provisioningIdentity.id, 'FoundryUser', deploymentSuffix)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '53ca6127-db72-4b80-b1b0-d745d6d5456d')
    principalId: provisioningIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [
    foundryProject
  ]
}

output apiIdentityId string = apiIdentity.id
output apiIdentityClientId string = apiIdentity.properties.clientId
output apiIdentityPrincipalId string = apiIdentity.properties.principalId
output mcpIdentityId string = fabricUami.id
output mcpIdentityClientId string = fabricUami.properties.clientId
output mcpIdentityPrincipalId string = fabricUami.properties.principalId
output provisioningIdentityId string = provisioningIdentity.id
output provisioningIdentityClientId string = provisioningIdentity.properties.clientId
output provisioningIdentityPrincipalId string = provisioningIdentity.properties.principalId
