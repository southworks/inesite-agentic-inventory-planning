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

| Parameter | Type | Required |
|-----------|------|----------|
| `caseId` | string | yes |
| `executionId` | string | yes |

Demo cases: `case-01` … `case-05` (see [`dataset-seed/README.md`](../../dataset-seed/README.md)).

Case-scoped signal data is read from `dataset-seed/cases/{caseId}/fabric-pre-requisite-data/`.

## Responsibilities

- Read planning signals from local dataset assets or Fabric Lakehouse (configurable via `DataSource:Mode`)
- Retrieve **policies** from a **Foundry IQ** knowledge base backed by Azure AI Search
- Search **signal evidence** lexically over case-scoped `fabric-pre-requisite-data` JSON files
- Serve other RAG knowledge (promotions, signal quality, trend patterns) from local text files under `dataset-seed/`

## Foundry IQ retrieval (policies only)

Policy grounding uses a Foundry IQ knowledge base:

| Knowledge base | Purpose |
| --- | --- |
| `inventory-policy-knowledge-kb` | Policies for `get_relevant_policies`, `get_planning_constraints`, `get_policies_by_refs` |

The policy knowledge source and base are created during deploy by the Foundry IQ bootstrap job (see below). Signal evidence is **not** pre-indexed; `search_signal_evidence` reads the local case dataset directly.

The MCP runtime only queries the policy knowledge base; it does not embed, rerank, or seed indexes at startup.

## Configuration

Base settings live in [appsettings.json](appsettings.json). Optional local overrides (gitignored):

| File | Purpose |
| --- | --- |
| `appsettings.Development.json` | Local dev — Foundry IQ Search endpoint |
| `appsettings.Deployment.local.json` | Deploy-style overrides (Container Apps parity) |
| `appsettings.Bootstrap.local.json` | Foundry IQ bootstrap runs (copy from example below) |

Example runtime shape (see [appsettings.json](appsettings.json) for defaults):

```json
{
  "Dataset": {
    "RootPath": "../../../dataset-seed",
    "CasesRelativePath": "cases",
    "FabricPrerequisiteSubfolder": "fabric-pre-requisite-data",
    "PromotionsFilePath": "../../../dataset-seed/promotions-price-rag/promotions_price_calendar.txt",
    "SignalQualityFilePath": "../../../dataset-seed/signal-quality-rag/signal_quality_rules.txt",
    "TrendPatternsFilePath": "../../../dataset-seed/trend-patterns-rag/trend_patterns.txt"
  },
  "FoundryIq": {
    "SearchEndpoint": "https://{search-service}.search.windows.net",
    "PolicyKnowledgeBaseName": "inventory-policy-knowledge-kb",
    "PolicyKnowledgeSourceName": "inventory-policy-knowledge-ks"
  }
}
```

Leave API keys empty in Azure to use managed identity (`DefaultAzureCredential`).

Environment variables use the `__` separator (for example `FoundryIq__SearchEndpoint`, `Dataset__RootPath`).

## Local Development

```powershell
cd backend/GrokInventoryAndTrend.Mcp
dotnet run
```

Default URL: `http://localhost:5040`

Health check: `GET /health`

Put Foundry IQ Search settings in `appsettings.Development.json`, or set them via environment variables.

## Foundry IQ bootstrap

Policies from `policies.json` are pushed into an Azure AI Search index, registered as a search-index knowledge source, and linked to a knowledge base during deploy. To run bootstrap locally without starting the web host:

```powershell
cd backend/GrokInventoryAndTrend.Mcp
copy appsettings.Bootstrap.local.example.json appsettings.Bootstrap.local.json
# Edit appsettings.Bootstrap.local.json — Search endpoint, Foundry account URI, policies.json path
dotnet run -- --bootstrap-foundry-iq
```

[appsettings.Bootstrap.local.example.json](appsettings.Bootstrap.local.example.json) includes the full bootstrap configuration surface.

The host loads `appsettings.Bootstrap.local.json` automatically when running `--bootstrap-foundry-iq`.

In Azure, [infra/modules/container-jobs.bicep](../../infra/modules/container-jobs.bicep) runs a Container Apps Job with:

```text
dotnet GrokInventoryAndTrend.Mcp.dll --bootstrap-foundry-iq
```

[infra/modules/post-deploy-scripts.bicep](../../infra/modules/post-deploy-scripts.bicep) starts this job and waits for completion before agent provisioning.

## Container Image

The MCP image is built from [Dockerfile](Dockerfile). It bundles:

- the MCP host application
- `dataset-seed/` content including `policies.json` (bootstrap) and case evidence JSON (runtime search)

Container paths use `/app/dataset-seed` (see Dockerfile `ENV Dataset__*` entries).

## Azure Deployment

[infra/modules/container-apps.bicep](../../infra/modules/container-apps.bicep) deploys the MCP host as a Container App. Foundry agents call its public HTTPS MCP endpoints directly:

- `{mcpUrl}/signal-ingestion/mcp`
- `{mcpUrl}/feature-and-causality/mcp`
- `{mcpUrl}/forecasting/mcp`
- `{mcpUrl}/replenishment-and-allocation/mcp`
- `{mcpUrl}/planner-copilot/mcp`

Runtime configuration is injected as environment variables (Foundry IQ, dataset paths, managed identity via `AZURE_CLIENT_ID`).
