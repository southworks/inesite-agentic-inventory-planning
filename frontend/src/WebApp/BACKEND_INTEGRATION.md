# Backend Integration Playbook

This document guides humans and LLMs when connecting **Cohere.InventoryAndTrend.WebApp** to the real Planning orchestrator API.

For a UI-button-centric view of which endpoints each page action would call, see [`../../UI_ENDPOINT_MAPPING.md`](../../UI_ENDPOINT_MAPPING.md).

## Architecture reminder

```
Backend wire DTOs  →  PlanningApiClient + BackendWorkflowMapper + AgentOutputParser  →  UI DTOs
```

**Do not modify** Razor components, `State/`, or `Models/WorkflowStageUi.cs` unless new UI fields are explicitly requested.

## Step 0 — Gather backend artifacts

Obtain from the backend team:

- OpenAPI/Swagger spec or Postman collection
- Sample JSON for: start workflow, poll status (running, awaiting approval, completed, failed), submit human decision, errors
- Status enum values and stage key names
- How agent outputs are returned (object, JSON string, nested path)

Save samples under `frontend/tests/WebApp.Tests/Fixtures/Backend/`.

## Step 1 — Update wire DTOs

Edit `Contracts/Api/Backend/InventoryPlanningBackendContracts.cs` to match the backend exactly. Use `[JsonPropertyName]` for naming differences.

## Step 2 — Implement PlanningApiClient

Implement all `IPlanningApiClient` methods in `Services/PlanningApiClient.cs`:

| Method | Provisional endpoint |
|--------|---------------------|
| `StartWorkflowAsync` | `POST /api/inventory-planning/plans/{planId}/workflow/start` |
| `GetWorkflowStatusAsync` | `GET /api/inventory-planning/executions/{executionId}/status` |
| `SubmitHumanDecisionAsync` | `POST /api/inventory-planning/plans/{planId}/executions/{executionId}/resume` |

Use `ApiProblemDetails.EnsureSuccessOrThrowAsync` for failures.

## Step 3 — Implement BackendWorkflowMapper

Map wire responses → `WorkflowProgressResponse`, `PlanDetailResponse`, etc.

Normalise:

- **Status** → `Pending`, `Running`, `AwaitingHumanApproval`, `Completed`, `Failed`
- **Stage keys** → `SignalIngestion`, `FeatureAndCausality`, `Forecasting`, `ReplenishmentAndAllocation`, `PlannerCopilot`

## Step 4 — Update AgentOutputParser (if needed)

Keep semantic fields: every stage needs `summary`, `decision`, `evidence`. Forecasting and Planner Copilot use extended schemas from `agent-provisioning/shared/`.

## Step 5 — Switch configuration

```json
{
  "PlanningApi": {
    "Mode": "Remote",
    "BaseUrl": "https://your-backend-fqdn/"
  }
}
```

`Program.cs` registers `HttpClient<IPlanningApiClient, PlanningApiClient>` when `Mode=Remote`.

## Step 6 — Tests (mandatory)

- `BackendWorkflowMapperTests` with backend fixture JSON
- `PlanningApiClientTests` with mocked `HttpMessageHandler`
- `AgentOutputParserTests` for backend-format agent payloads
- Existing tests must still pass

Run: `dotnet test frontend/tests/WebApp.Tests/`

## Step 7 — Smoke test

1. Set `Mode=Remote` and valid `BaseUrl`
2. Home → scenario → Start workflow → verify polling
3. Human approval → terminal state
4. Compare with `Mode=Local` for visual parity

## Step 8 — Deployment

Inject environment variables:

- `PlanningApi__Mode=Remote`
- `PlanningApi__BaseUrl=https://...`

Keep `dataset-seed` in the container for demo scenarios.

## LLM checklist

1. Read this file, `IPlanningApiClient.cs`, `PlanContracts.cs`, `AgentContracts.cs`, backend OpenAPI/samples
2. Only edit wire DTOs, `PlanningApiClient`, `BackendWorkflowMapper`, `AgentOutputParser`, `Program.cs`, appsettings
3. Preserve `IPlanningApiClient` interface
4. Map, don't match — assume JSON differs from seed files
5. Keep Local mode working
6. All tests pass before finishing
