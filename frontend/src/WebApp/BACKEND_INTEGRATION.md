# Backend Integration Playbook

This document guides humans and LLMs when working with **GrokInventoryAndTrend.WebApp** and the Planning orchestrator API.

For a UI-button-centric view, see [`../../UI_ENDPOINT_MAPPING.md`](../../UI_ENDPOINT_MAPPING.md).

## Architecture

```
Backend wire DTOs  →  PlanningApiClient + BackendWorkflowMapper + AgentOutputParser  →  UI DTOs
```

**Do not modify** Razor components, `State/`, or `Models/WorkflowStageUi.cs` unless new UI fields are explicitly requested.

## Backend contract

| Method | Path | Response |
|--------|------|----------|
| `POST` | `/api/inventory-planning/cases/{caseId}/workflow/basic/start` | `BackendBasicWorkflowStatusResponse` |
| `GET` | `/api/inventory-planning/executions/{executionId}/basic/status` | `BackendBasicWorkflowStatusResponse` |
| `GET` | `/api/inventory-planning/cases/{caseId}/documents` | Not wired to UI yet |
| `GET` | `/api/inventory-planning/cases/{caseId}/documents/content?documentPath=...` | Not wired to UI yet |

Supported case ids: `case-01` through `case-05`.

There is **no** backend endpoint for listing scenarios, creating plans, or human approval. The frontend handles these client-side:

- **Scenarios:** `dataset-seed/cases/catalog.json` (repo root)
- **Plans:** `PlanSessionStore`
- **Human decision:** client-side after backend `Completed`

## Local development runbook

### Terminal 1 — Backend (`:5038`)

```powershell
cd backend/Api.Host

$env:AZURE_FOUNDRY_PROJECT_ENDPOINT = "https://your-project.services.ai.azure.com/api/projects/your-project"
$env:Dataset__RootPath = "C:\cohere\inesite-agentic-inventory-planning\dataset-seed"

dotnet run --launch-profile http
```

### Terminal 2 — Frontend (`:5147`)

```powershell
cd frontend/src/WebApp
dotnet run --launch-profile http
```

Open http://localhost:5147. Default `PlanningApi:BaseUrl` is `http://localhost:5038/`.

### Configuration

```json
{
  "PlanningApi": {
    "BaseUrl": "http://localhost:5038/"
  },
  "DatasetSeed": {
    "RootPath": "../../../dataset-seed"
  },
  "WorkflowPolling": {
    "IntervalSeconds": 2,
    "MaxDurationMinutes": 25
  }
}
```

## Key services

| Service | Role |
|---------|------|
| `PlanningApiClient` | HTTP facade implementing `IPlanningApiClient` |
| `BackendCaseCatalogService` | Loads `cases/catalog.json` for Home page |
| `BackendWorkflowMapper` | Maps flat `agentOutputs` → UI stages |
| `AgentOutputParser` | Parses raw agent JSON strings |
| `PlanSessionStore` | In-memory plan sessions |

## Mapper behaviour

- Synthesises five stages from `agentOutputs.*`
- Backend `Completed` → UI `AwaitingHumanApproval` (until reviewer decides)
- Backend `Failed` → UI `Failed` with `failureReason`
- After client decision: `Approve`/`ApproveWithAdjustments` → `Completed`, `Reject` → `Failed`

## Tests

Fixtures: `frontend/tests/WebApp.Tests/Fixtures/Backend/`

```powershell
dotnet test frontend/tests/WebApp.Tests/GrokInventoryAndTrend.WebApp.Tests.csproj
```

## Smoke test

1. Start backend and frontend (two terminals)
2. Home → 5 cases → Start planning run → Start workflow
3. Wait for polling → approval panel
4. Approve or Reject → terminal outcome
