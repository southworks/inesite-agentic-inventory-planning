param location string
param resourceTags object
param searchServiceName string
param searchSku string
// TODO: enable when Document Intelligence is needed
// param documentIntelligenceAccountName string

resource searchService 'Microsoft.Search/searchServices@2023-11-01' = {
  name: searchServiceName
  location: location
  tags: resourceTags
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: searchSku
  }
  properties: {
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'default'
    publicNetworkAccess: 'enabled'
    semanticSearch: 'free'
    authOptions: {
      aadOrApiKey: {
        aadAuthFailureMode: 'http401WithBearerChallenge'
      }
    }
  }
}

// TODO: enable when Document Intelligence is needed
// resource documentIntelligenceAccount 'Microsoft.CognitiveServices/accounts@2023-05-01' = {
//   name: documentIntelligenceAccountName
//   location: location
//   tags: resourceTags
//   kind: 'FormRecognizer'
//   sku: {
//     name: 'S0'
//   }
//   properties: {
//     customSubDomainName: documentIntelligenceAccountName
//     publicNetworkAccess: 'Enabled'
//   }
// }

output searchServiceName string = searchService.name
output searchServiceId string = searchService.id
output searchServiceEndpoint string = 'https://${searchService.name}.search.windows.net'
output searchServicePrincipalId string = searchService.identity.principalId
// TODO: enable when Document Intelligence is needed
// output documentIntelligenceAccountName string = documentIntelligenceAccount.name
// output documentIntelligenceAccountId string = documentIntelligenceAccount.id
// output documentIntelligenceEndpoint string = documentIntelligenceAccount.properties.endpoint
