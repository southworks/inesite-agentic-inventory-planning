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
| `/planner-copilot/mcp` | Planner Copilot | `get_planning_constraints` |

## Tool parameters

Every tool:

| Parámetro | Tipo | Requerido |
|-----------|------|-----------|
| `caseId` | string | sí |
| `executionId` | string | sí |

Demo case: `case-01` … `case-05`

Case-scoped signal data is read from `dataset-seed/cases/{caseId}/fabric-pre-requisite-data/`.

## Azure AI Search

The `inventory-signal-evidence` index is provisioned but **not populated yet**. `search_signal_evidence` tries Azure AI Search first; when the index has no matches (current demo state), it falls back to lexical search over the case `fabric-pre-requisite-data` files. Primary signal retrieval for agents is `get_planning_signals`.

## Local Development

```powershell
cd backend/GrokInventoryAndTrend.Mcp
dotnet run
```

Default URL: `http://localhost:5040`

Health check: `GET /health`
