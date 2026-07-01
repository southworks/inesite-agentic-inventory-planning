param baseName string

var deploymentSuffix = uniqueString(resourceGroup().id)

output deploymentSuffix string = deploymentSuffix
output foundryAccountName string = toLower(take(replace('${baseName}foundry${deploymentSuffix}', '-', ''), 24))
output searchServiceName string = toLower(take(replace('${baseName}search${deploymentSuffix}', '-', ''), 60))
output logAnalyticsName string = take('${baseName}-logs-${deploymentSuffix}', 63)
output containerAppsEnvironmentName string = take('${baseName}-cae-${deploymentSuffix}', 63)
output apiAppName string = take('${baseName}-api-${deploymentSuffix}', 32)
output mcpAppName string = take('${baseName}-mcp-${deploymentSuffix}', 32)
output frontendAppName string = take('${baseName}-web-${deploymentSuffix}', 32)
output foundryIqBootstrapJobName string = take('${baseName}-iqbootstrap-${deploymentSuffix}', 32)
output provisioningJobName string = take('${baseName}-provision-${deploymentSuffix}', 32)
output apiIdentityName string = '${baseName}-api-identity-${deploymentSuffix}'
output mcpIdentityName string = '${baseName}-mcp-identity-${deploymentSuffix}'
output provisioningIdentityName string = '${baseName}-provision-identity-${deploymentSuffix}'
output deploymentScriptIdentityName string = '${baseName}-deployscript-${deploymentSuffix}'
