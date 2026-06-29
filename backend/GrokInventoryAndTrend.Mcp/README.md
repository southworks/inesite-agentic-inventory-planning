# Inventory Planning MCP Server

Demo-grade MCP tool provider for the five-agent inventory planning and trend forecasting workflow.

All tools accept only **`caseId`** and **`executionId`** for this Foundry + Grok 4.3 demo.

## MCP Endpoints

| MCP endpoint | Agent | Tools |
| --- | --- | --- |
| `/signal-ingestion/mcp` | Signal ingestion | `get_planning_signals`, `search_signal_evidence`, `get_signal_quality_rules` |
| `/feature-and-causality/mcp` | Feature & causality | `get_planning_profile`, `search_signal_evidence`, `get_driver_context`, `get_relevant_promotions` |
| `/forecasting/mcp` | Forecasting | `search_signal_evidence`, `get_trend_patterns`, `get_relevant_promotions`, `get_forecasting_context` |
| `/replenishment-and-allocation/mcp` | Replenishment & allocation | `get_replenishment_signals`, `build_replenishment_recommendations` |
| `/planner-copilot/mcp` | Planner Copilot | `get_planning_constraints`, `get_relevant_policies`, `get_policies_by_refs` |

## Tool parameters

Every tool:

| Parámetro | Tipo | Requerido |
|-----------|------|-----------|
| `caseId` | string | sí |
| `executionId` | string | sí |

Demo cases: `case-01` … `case-05` (see [`dataset-seed/README.md`](../../dataset-seed/README.md)).

Case-scoped signal data is read from `dataset-seed/cases/{caseId}/fabric-pre-requisite-data/`.

## Responsibilities

- Read planning signals from local dataset assets or Fabric Lakehouse (configurable via `DataSource:Mode`)
- Ensure Azure AI Search evidence and policy indexes exist on startup when `McpStartup:EnsureSearchIndexesOnStartup` is enabled
- Seed the policy index from [`dataset-seed/policy_rag.txt`](../../dataset-seed/policy_rag.txt) during deploy-time seeding (or when `McpStartup:SeedPoliciesOnStartup` is enabled)
- Reindex policies only when the policy source hash changes
- Retrieve signal evidence and policies from Azure AI Search with Foundry embeddings and Cohere rerank
- Serve other RAG knowledge (promotions, signal quality, trend patterns) from local text files under `dataset-seed/`
- Batch embedding inputs, limit Foundry concurrency, and retry transient throttling/server errors

## Azure AI Search

The `inventory-signal-evidence` index is provisioned but **not populated yet**. `search_signal_evidence` tries Azure AI Search first; when the index has no matches (current demo state), it falls back to lexical search over the case `fabric-pre-requisite-data` files. Primary signal retrieval for agents is `get_planning_signals`.

## Configuration

Base settings live in [appsettings.json](appsettings.json). Optional local overrides (gitignored):

| File | Purpose |
| --- | --- |
| `appsettings.Development.json` | Local dev — Azure Search and Foundry endpoints |
| `appsettings.Deployment.local.json` | Deploy-style overrides (Container Apps parity) |
| `appsettings.Seed.local.json` | Policy seed runs (copy from example below) |

Example shape (see [appsettings.json](appsettings.json) for defaults):

```json
{
  "McpStartup": {
    "EnsureSearchIndexesOnStartup": true,
    "SeedPoliciesOnStartup": false
  },
  "Dataset": {
    "RootPath": "../../../dataset-seed",
    "PolicyFilePath": "../../../dataset-seed/policy_rag.txt",
    "CasesRelativePath": "cases",
    "FabricPrerequisiteSubfolder": "fabric-pre-requisite-data",
    "PromotionsFilePath": "../../../dataset-seed/promotions-price-rag/promotions_price_calendar.txt",
    "SignalQualityFilePath": "../../../dataset-seed/signal-quality-rag/signal_quality_rules.txt",
    "TrendPatternsFilePath": "../../../dataset-seed/trend-patterns-rag/trend_patterns.txt"
  },
  "AzureSearch": {
    "Endpoint": "https://{search-service}.search.windows.net",
    "EvidenceIndexName": "inventory-signal-evidence",
    "PolicyIndexName": "inventory-policy-knowledge",
    "VectorDimensions": 1536
  },
  "AzureFoundryModels": {
    "EmbedDeploymentName": "text-embedding-3-small",
    "RerankDeploymentName": "Cohere-rerank-v4.0-fast",
    "EmbedEndpoint": "https://{account}.services.ai.azure.com",
    "RerankEndpoint": "https://{account}.services.ai.azure.com/providers/cohere/v2/rerank",
    "ApiKey": "",
    "EmbeddingDimensions": 1536
  }
}
```

Leave `ApiKey` empty in Azure to use managed identity (`DefaultAzureCredential`).

**Endpoint notes**

- `EmbedEndpoint` — Foundry account or hub deployment base used for `/embeddings`.
- `RerankEndpoint` — Foundry account base; the app calls `/providers/cohere/v2/rerank` on that URL.

Environment variables use the `__` separator (for example `AzureSearch__Endpoint`, `Dataset__PolicyFilePath`).

## Local Development

```powershell
cd backend/GrokInventoryAndTrend.Mcp
dotnet run
```

Default URL: `http://localhost:5040`

Health check: `GET /health`

Put Azure Search and Foundry values in `appsettings.Development.json`, or set them via environment variables.

## Policy index seeding

Policies are sourced from [`dataset-seed/policy_rag.txt`](../../dataset-seed/policy_rag.txt) (`Policy Ref:` blocks). To run deploy-style seeding locally without starting the web host:

```powershell
cd backend/GrokInventoryAndTrend.Mcp
copy appsettings.Seed.local.example.json appsettings.Seed.local.json
# Edit appsettings.Seed.local.json — Search endpoint, index names, Foundry embed/rerank
dotnet run -- --seed-policies
```

[appsettings.Seed.local.example.json](appsettings.Seed.local.example.json) includes higher retry limits suited to batch embedding (`MaxRetryAttempts: 10`, `MaxDelaySeconds: 60`).

The host loads `appsettings.Seed.local.json` automatically when the file exists (alongside `appsettings.Deployment.local.json`).

In Azure, [infra/modules/container-jobs.bicep](../../infra/modules/container-jobs.bicep) runs a Container Apps Job with:

```text
dotnet GrokInventoryAndTrend.Mcp.dll --seed-policies
```

Planner Copilot tools read from the seeded policy index after seeding completes.

## Container Image

The MCP image is built from [Dockerfile](Dockerfile). It bundles:

- the MCP host application
- `dataset-seed/` content including `policy_rag.txt`

Container paths use `/app/dataset-seed` (see Dockerfile `ENV Dataset__*` entries).

## Azure Deployment

[infra/modules/container-apps.bicep](../../infra/modules/container-apps.bicep) deploys the MCP host as a Container App. Foundry agents call its public HTTPS MCP endpoints directly:

- `{mcpUrl}/signal-ingestion/mcp`
- `{mcpUrl}/feature-and-causality/mcp`
- `{mcpUrl}/forecasting/mcp`
- `{mcpUrl}/replenishment-and-allocation/mcp`
- `{mcpUrl}/planner-copilot/mcp`

Runtime configuration is injected as environment variables (Search, Foundry, dataset paths, managed identity via `AZURE_CLIENT_ID`).
