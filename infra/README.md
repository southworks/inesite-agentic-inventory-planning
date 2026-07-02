# Infrastructure

Azure deployment for the agentic inventory planning demo. The entry point is [main.bicep](main.bicep), compiled to [azuredeploy.json](azuredeploy.json) for the **Deploy to Azure** button and ARM deployments.

## Deploy

**Portal (recommended):** use the [Deploy to Azure](../README.md#deploy-to-azure) button in the root README. The custom UI is defined in [createUiDefinition.json](createUiDefinition.json).

**CLI:**

```powershell
az group create -n rg-inventory-demo -l eastus

az deployment group create `
  -g rg-inventory-demo `
  -f infra/main.bicep `
  -p @infra/main.parameters.json
```

Regenerate the ARM template after Bicep changes:

```powershell
az bicep build --file infra/main.bicep --outfile infra/azuredeploy.json
```

## Module layout

| Module | Purpose |
| --- | --- |
| [naming.bicep](modules/naming.bicep) | Deterministic resource names from `baseName` |
| [platform.bicep](modules/platform.bicep) | Log Analytics, Application Insights, Container Apps environment |
| [data-services.bicep](modules/data-services.bicep) | Azure AI Search |
| [foundry.bicep](modules/foundry.bicep) | Foundry account, project, Grok 4.3 + embedding deployments |
| [security.bicep](modules/security.bicep) | Managed identities and RBAC |
| [container-apps.bicep](modules/container-apps.bicep) | API, MCP, and Blazor frontend Container Apps |
| [container-jobs.bicep](modules/container-jobs.bicep) | Foundry IQ bootstrap and agent provisioning jobs |
| [post-deploy-scripts.bicep](modules/post-deploy-scripts.bicep) | Starts jobs and waits for completion |
| [fabric-provision.bicep](modules/fabric-provision.bicep) | Fabric lakehouse (when `enableFabric=true`) |
| [fabric-seed.bicep](modules/fabric-seed.bicep) | Uploads `dataset-seed` to Fabric (when enabled) |

## Parameters

### Core and models

| Parameter | Default | Description |
| --- | --- | --- |
| `baseName` | `grokinventory` | Prefix for deployed resource names |
| `modelDeploymentName` | `grok-4.3` | Foundry deployment name used by all agents |
| `modelDeploymentSkuName` | `GlobalStandard` | SKU for the agent model deployment |
| `modelDeploymentCapacity` | `100` | Capacity units for the agent model |
| `agentModelFormat` | `xAI` | Provider format for Grok 4.3 |
| `agentModelName` | `grok-4.3` | Model name in the Foundry catalog |
| `agentModelVersion` | `1` | Model version in the Foundry catalog |
| `searchSku` | `standard` | Azure AI Search SKU |

Embedding model settings (`text-embedding-3-small`, 1536 dimensions) are fixed in [foundry.bicep](modules/foundry.bicep) and are not deployment parameters.

### Container images

| Parameter | Default |
| --- | --- |
| `apiContainerImage` | `ghcr.io/southworks/inventoryplanning-api:demo` |
| `mcpContainerImage` | `ghcr.io/southworks/inventoryplanning-mcp:demo` |
| `provisioningContainerImage` | `ghcr.io/southworks/inventoryplanning-provisioning:demo` |
| `frontendContainerImage` | `ghcr.io/southworks/inventoryplanning-web:demo` |

### Fabric (optional — at end of parameter list)

| Parameter | Default | Description |
| --- | --- | --- |
| `enableFabric` | `false` | When `false`, MCP uses bundled **Local** dataset mode |
| `fabricWorkspaceName` | `''` | Required when Fabric is enabled |
| `fabricLakehouseName` | `InventoryPlanningLakehouse` | Lakehouse to create or reuse |
| `fabricUamiResourceId` | `''` | UAMI from [setup-fabric-provision-identity.ps1](scripts/setup-fabric-provision-identity.ps1) |
| `enableFabricSeed` | `true` | Upload demo data to the lakehouse |
| `fabricRepositoryArchiveUrl` | GitHub `main` archive URL | Source for seed script |

## Deployment outputs

### Primary (day-one validation)

| Output | Description |
| --- | --- |
| `retailSiteUrl` | Blazor retail planning UI |
| `foundryProjectUrl` | Azure Portal link to the Foundry project |
| `appInsightsLiveMetricsUrl` | Application Insights Live Metrics |
| `apiUrl` | Planning orchestrator API |
| `mcpUrl` | MCP host base URL |
| `frontendUrl` | Same host as `retailSiteUrl` without trailing slash |

### Foundry and Search

| Output | Description |
| --- | --- |
| `foundryAccountName` | Cognitive Services account name |
| `foundryProjectName` | Foundry project name |
| `foundryProjectEndpoint` | Project API endpoint |
| `foundryProjectResourceId` | ARM resource ID |
| `modelDeploymentName` | Grok deployment name |
| `embedDeploymentName` | Embedding deployment name |
| `embedModelName` | Embedding model name |
| `searchServiceName` | AI Search service name |
| `searchServiceEndpoint` | Search HTTPS endpoint |

### Fabric (empty when `enableFabric=false`)

| Output | Description |
| --- | --- |
| `fabricWorkspaceId` | Fabric workspace ID |
| `fabricWorkspaceName` | Workspace display name |
| `fabricLakehouseId` | Lakehouse ID |
| `fabricLakehouseName` | Lakehouse name |
| `fabricSqlServer` | SQL analytics endpoint server |
| `fabricSqlDatabase` | SQL analytics database name |

### Operations

| Output | Description |
| --- | --- |
| `containerAppsEnvironmentId` | Container Apps environment |
| `provisioningJobName` | Agent provisioning job name |
| `foundryIqBootstrapJobName` | Foundry IQ bootstrap job name |

## Post-deploy sequence

1. Container Apps (API, MCP, frontend) become reachable.
2. [post-deploy-scripts.bicep](modules/post-deploy-scripts.bicep) runs the Foundry IQ bootstrap job and waits for success.
3. The same module runs the agent provisioning job and waits for success.
4. If Fabric is enabled, [fabric-seed.bicep](modules/fabric-seed.bicep) may upload demo data (when `enableFabricSeed=true`).

Total time is typically 15–30 minutes depending on region and job retries.

## Scripts

| Script | Purpose |
| --- | --- |
| [setup-fabric-provision-identity.ps1](scripts/setup-fabric-provision-identity.ps1) | Create UAMI and assign Fabric workspace role (required before Fabric-enabled deploy) |
| [seed-fabric-data.ps1](scripts/seed-fabric-data.ps1) | Download repo archive and upload `dataset-seed` to Fabric (invoked by Bicep) |
| [provision-fabric-lakehouse.ps1](scripts/provision-fabric-lakehouse.ps1) | Create Fabric lakehouse (invoked by Bicep) |
