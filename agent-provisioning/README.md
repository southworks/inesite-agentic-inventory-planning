# Agent Provisioning

This project provisions the five Azure AI Foundry prompt agents required by the agentic inventory planning and trend forecasting workflow.

In a standard Azure deployment flow, provisioning runs automatically as a Container Apps Job after infrastructure and MCP wiring complete. You do not need to run this CLI manually after deployment.

## Workflow Sequence

The planning orchestrator coordinates these agents in order:

1. `signal-ingestion-agent` — ingest and validate real-time signals
2. `feature-and-causality-agent` — build predictors and measure driver impact
3. `forecasting-agent` — produce short-term demand forecasts and detect anomalies
4. `replenishment-and-allocation-agent` — recommend inventory targets and PO/TO orders
5. `planner-copilot-agent` — validate budget and service-level constraints for human approval

The **Planning agent** (orchestrator) is external to this provisioning project.

## Agents

| Agent | Responsibility | MCP path |
| --- | --- | --- |
| `signal-ingestion-agent` | Ingest real-time signals and validate data quality | `/signal-ingestion/mcp` |
| `feature-and-causality-agent` | Build events and predictors; test driver impact | `/feature-and-causality/mcp` |
| `forecasting-agent` | Short-term demand forecast; detect shifts and anomalies | `/forecasting/mcp` |
| `replenishment-and-allocation-agent` | Recommend targets and draft PO/TO orders | `/replenishment-and-allocation/mcp` |
| `planner-copilot-agent` | Enforce budget and service-level constraints (HITL) | `/planner-copilot/mcp` |

All agents use the Foundry model deployment named by `AZURE_AI_MODEL_DEPLOYMENT_NAME`.

## Azure Deployment Lifecycle

When infrastructure is deployed, the typical flow:

1. Provisions Foundry, Storage, Search, and model deployments.
2. Deploys the MCP Container App.
3. Starts the agent provisioning Container Apps Job.
4. Waits for the job to finish before completing the deployment.

The provisioning container image is built from [Dockerfile](Dockerfile) and runs:

- prompt agent create/update with direct public MCP tools
- strict JSON schema output configuration
- idempotent version creation based on definition fingerprints

## Agent-as-Code Layout

Agent assets live inside the provisioning project:

```text
src/GrokInventoryAndTrend.AgentProvisioning/
  agents/
    signal-ingestion-agent/
    feature-and-causality-agent/
    forecasting-agent/
    replenishment-and-allocation-agent/
    planner-copilot-agent/
  shared/
    agent-structured-output.schema.json
    forecasting-structured-output.schema.json
    planner-copilot-structured-output.schema.json
```

`dotnet publish` copies `agents/` and `shared/` next to the executable. The container image only needs the published output.

## Configuration

Required environment variables:

- `AZURE_FOUNDRY_PROJECT_ENDPOINT`
- `AZURE_AI_MODEL_DEPLOYMENT_NAME`
- `MCP_BASE_URL`

## Optional Local Maintenance

Use this only when updating agent definitions outside the Azure deployment flow:

```powershell
$env:AZURE_FOUNDRY_PROJECT_ENDPOINT = "https://..."
$env:AZURE_AI_MODEL_DEPLOYMENT_NAME = "grok-4.3"
$env:MCP_BASE_URL = "https://..."

./agent-provisioning/scripts/provision-agents.ps1
```

Or:

```powershell
dotnet run --project agent-provisioning/src/GrokInventoryAndTrend.AgentProvisioning
```

## Idempotency and Fail-Fast Behavior

For each agent the CLI:

1. Loads manifest assets.
2. Validates required model deployment and MCP base URL configuration.
3. Reads the latest Foundry agent version when present.
4. Compares a deterministic definition fingerprint.
5. Creates a new version only when the definition changed.

Results are reported as `Created`, `Updated`, `Unchanged`, or `Failed`.

## Structured Output Contract

Each agent returns JSON with at minimum:

- `summary`
- `decision`
- `evidence`

The shared schema lives in [src/GrokInventoryAndTrend.AgentProvisioning/shared/agent-structured-output.schema.json](src/GrokInventoryAndTrend.AgentProvisioning/shared/agent-structured-output.schema.json).

`forecasting-agent` uses an extended strict schema in [src/GrokInventoryAndTrend.AgentProvisioning/shared/forecasting-structured-output.schema.json](src/GrokInventoryAndTrend.AgentProvisioning/shared/forecasting-structured-output.schema.json) that also requires:

- `confidenceLevel`
- `anomalies`
- `keyMetrics`

`planner-copilot-agent` uses [src/GrokInventoryAndTrend.AgentProvisioning/shared/planner-copilot-structured-output.schema.json](src/GrokInventoryAndTrend.AgentProvisioning/shared/planner-copilot-structured-output.schema.json) with:

- `approvalAssessment`
- `budgetImpact`
- `serviceLevelImpact`
- `concerns`
- `recommendations`

## Example Use Case

**Input:** "Plan inventory for the summer campaign in category X."

1. The orchestrator distributes work across agents.
2. `signal-ingestion-agent` pulls sales, promotions, and inventory; validates quality.
3. `feature-and-causality-agent` identifies drivers (price, promo, seasonality).
4. `forecasting-agent` projects demand and detects anomalies.
5. `replenishment-and-allocation-agent` proposes stock targets and draft PO/TO orders.
6. `planner-copilot-agent` checks budget and service-level constraints; a human planner approves or adjusts.
