param location string
param resourceTags object
param deploymentSuffix string
param nameSuffix string
param deploymentScriptIdentityName string
param foundryAccountName string
param foundryProjectName string
param foundryIqBootstrapJobName string
param provisioningJobName string

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: foundryAccountName
}

resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' existing = {
  parent: foundryAccount
  name: foundryProjectName
}

resource deploymentScriptIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: deploymentScriptIdentityName
  location: location
  tags: resourceTags
}

resource deploymentScriptContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, deploymentScriptIdentity.id, 'Contributor', nameSuffix)
  scope: resourceGroup()
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b24988ac-6180-42a0-ab88-20f7382dd24c')
    principalId: deploymentScriptIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource deploymentScriptFoundryContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, deploymentScriptIdentity.id, 'CognitiveServicesContributor', nameSuffix)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '25fbc0a9-bd7c-42a3-aa1a-3b75d497ee68')
    principalId: deploymentScriptIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [
    foundryProject
  ]
}

resource deploymentScriptFoundryUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, deploymentScriptIdentity.id, 'FoundryUser', nameSuffix)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '53ca6127-db72-4b80-b1b0-d745d6d5456d')
    principalId: deploymentScriptIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [
    foundryProject
  ]
}

resource runFoundryIqBootstrapScript 'Microsoft.Resources/deploymentScripts@2023-08-01' = {
  name: 'run-foundry-iq-bootstrap-${deploymentSuffix}'
  location: location
  tags: resourceTags
  kind: 'AzureCLI'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${deploymentScriptIdentity.id}': {}
    }
  }
  properties: {
    azCliVersion: '2.62.0'
    timeout: 'PT60M'
    retentionInterval: 'PT1H'
    cleanupPreference: 'OnSuccess'
    forceUpdateTag: deploymentSuffix
    scriptContent: '''
      set -euo pipefail
      az extension add --name containerapp --upgrade 2>/dev/null || true
      echo "Waiting for role assignments and Foundry deployments to settle..."
      sleep 180
      echo "Starting Foundry IQ bootstrap job..."
      EXECUTION=$(az containerapp job start --name "${FOUNDRY_IQ_BOOTSTRAP_JOB_NAME}" --resource-group "${RESOURCE_GROUP}" --query name -o tsv)
      echo "Foundry IQ bootstrap job execution: ${EXECUTION}"

      for i in $(seq 1 180); do
        STATUS=$(az containerapp job execution show \
          --name "${FOUNDRY_IQ_BOOTSTRAP_JOB_NAME}" \
          --resource-group "${RESOURCE_GROUP}" \
          --job-execution-name "${EXECUTION}" \
          --query properties.status -o tsv)

        echo "Foundry IQ bootstrap job status: ${STATUS}"

        if [ "${STATUS}" = "Succeeded" ]; then
          echo "Foundry IQ bootstrap completed successfully."
          exit 0
        fi

        if [ "${STATUS}" = "Failed" ]; then
          echo "Foundry IQ bootstrap job failed. Fetching recent job logs..."
          az containerapp job logs show \
            --name "${FOUNDRY_IQ_BOOTSTRAP_JOB_NAME}" \
            --resource-group "${RESOURCE_GROUP}" \
            --execution "${EXECUTION}" \
            --container foundry-iq-bootstrap \
            --tail 50 2>/dev/null || true
          exit 1
        fi

        sleep 15
      done

      echo "Timed out waiting for Foundry IQ bootstrap job."
      exit 1
    '''
    environmentVariables: [
      {
        name: 'RESOURCE_GROUP'
        value: resourceGroup().name
      }
      {
        name: 'FOUNDRY_IQ_BOOTSTRAP_JOB_NAME'
        value: foundryIqBootstrapJobName
      }
    ]
  }
  dependsOn: [
    deploymentScriptContributorRole
  ]
}

resource runProvisioningScript 'Microsoft.Resources/deploymentScripts@2023-08-01' = {
  name: 'run-agent-provisioning-${deploymentSuffix}'
  location: location
  tags: resourceTags
  kind: 'AzureCLI'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${deploymentScriptIdentity.id}': {}
    }
  }
  properties: {
    azCliVersion: '2.62.0'
    timeout: 'PT45M'
    retentionInterval: 'PT1H'
    cleanupPreference: 'OnSuccess'
    forceUpdateTag: deploymentSuffix
    scriptContent: '''
      set -euo pipefail
      az extension add --name containerapp --upgrade 2>/dev/null || true
      echo "Waiting for MCP health and role assignment propagation..."
      sleep 120
      echo "Starting inventory planning agent provisioning job..."
      EXECUTION=$(az containerapp job start --name "${PROVISIONING_JOB_NAME}" --resource-group "${RESOURCE_GROUP}" --query name -o tsv)
      echo "Job execution: ${EXECUTION}"

      for i in $(seq 1 120); do
        STATUS=$(az containerapp job execution show \
          --name "${PROVISIONING_JOB_NAME}" \
          --resource-group "${RESOURCE_GROUP}" \
          --job-execution-name "${EXECUTION}" \
          --query properties.status -o tsv)

        echo "Provisioning job status: ${STATUS}"

        if [ "${STATUS}" = "Succeeded" ]; then
          echo "Inventory planning agent provisioning completed successfully."
          exit 0
        fi

        if [ "${STATUS}" = "Failed" ]; then
          echo "Agent provisioning job failed."
          az containerapp job logs show \
            --name "${PROVISIONING_JOB_NAME}" \
            --resource-group "${RESOURCE_GROUP}" \
            --execution "${EXECUTION}" \
            --container agent-provisioning \
            --tail 50 2>/dev/null || true
          exit 1
        fi

        sleep 15
      done

      echo "Timed out waiting for agent provisioning job."
      exit 1
    '''
    environmentVariables: [
      {
        name: 'RESOURCE_GROUP'
        value: resourceGroup().name
      }
      {
        name: 'PROVISIONING_JOB_NAME'
        value: provisioningJobName
      }
    ]
  }
  dependsOn: [
    deploymentScriptContributorRole
    runFoundryIqBootstrapScript
  ]
}
