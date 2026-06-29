# inesite-agentic-inventory-planning

Reference implementation of the "Retail inventory planning and trend forecasting" use case
(Signal Ingestion → Feature & Causality → Forecasting → Replenishment & Allocation →
Planner Copilot, powered by Grok 4.3 in Azure AI Foundry).

Reference user story: [US 128593](https://dev.azure.com/southworks/inesite/_workitems/edit/128593).

## Deploy to Azure

The primary deployment path is a single end-to-end Azure deployment from the README button.

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fsouthworks%2Finesite-agentic-inventory-planning%2Fmain%2Finfra%2Fazuredeploy.json/createUiDefinition.uri/https%3A%2F%2Fraw.githubusercontent.com%2Fsouthworks%2Finesite-agentic-inventory-planning%2Fmain%2Finfra%2FcreateUiDefinition.json)

### Fabric prerequisites (required)

The MCP container app reads case data from a Microsoft Fabric Lakehouse, so a Fabric workspace is mandatory. Before clicking Deploy, prepare the UAMI that the Bicep will reuse as the MCP identity:

1. A Fabric workspace (capacity-backed). Note its name.
2. From the repo root, in a PowerShell 7 terminal, run:

   ```powershell
   ./infra/scripts/setup-fabric-provision-identity.ps1 `
     -ResourceGroupName <rg> `
     -WorkspaceName <fabric-ws> `
     -Location eastus `
     -FabricRole Contributor
   ```

   The script creates the user-assigned managed identity, assigns the workspace role, and prints the `managedIdentityResourceId`. The client ID is auto-derived by the deployment.

3. In the Deploy-to-Azure form, on the **Fabric prerequisites** step, paste that value along with the workspace and lakehouse names. The lakehouse is created at deploy time if it does not exist.

Without those values the deployment will fail at the Fabric seed step.

When you deploy:

1. Azure provisions Foundry, model deployments, Storage, Search, and Platform infrastructure.
2. A deployment script provisions the Fabric Lakehouse in the supplied workspace (always runs).
3. A deployment script seeds the lakehouse with case data from `dataset-seed/` (runs only when `enableFabricSeed=true`). Raw files go to `Files/raw/` and bronze tables to the Lakehouse SQL endpoint.
4. The deployment outputs the Fabric workspace and lakehouse IDs/names and the SQL endpoint.
5. Container Apps and post-deploy provisioning are disabled until container images are published.

Container images are published automatically to GitHub Container Registry by [.github/workflows/publish-container-images.yml](.github/workflows/publish-container-images.yml) on pushes to `main`. The deployment template references these default URIs:

- `ghcr.io/southworks/cohereinvandtrend-api:demo`
- `ghcr.io/southworks/cohereinvandtrend-mcp:demo`
- `ghcr.io/southworks/cohereinvandtrend-provisioning:demo`

Make the GHCR packages public after the first workflow run so Azure Container Apps can pull them without registry credentials.

### After deployment

Case data is read from the Fabric Lakehouse created during deployment. The deployment outputs `fabricWorkspaceName` and `fabricLakehouseName`. Use the Fabric portal to inspect or upload additional cases.

To skip the data upload (e.g., while you repair the workspace or the UAMI role assignment), redeploy `infra/main.bicep` with `enableFabricSeed=false`. The lakehouse is still provisioned (empty but functional).

## Dataset seed (demo)

[`dataset-seed/`](dataset-seed/) holds **demo-ready inputs only**: five e2e cases, case-scoped prerequisite entities, and Fabric upload payloads. Each case is self-contained — pick one, upload ingest files, submit the orchestrator request.

```
dataset-seed/
  cases/case-01 … case-05/         README + ingest/ + fabric-pre-requisite-data/
  cases/catalog.json               frontend Home page case list (metadata)
```

See [`dataset-seed/README.md`](dataset-seed/README.md) for the case index and quick start.

**Prerequisite:** upload each case's `ingest/` files to Fabric before testing. See [`data-generation/docs/TESTING_GUIDE.md`](data-generation/docs/TESTING_GUIDE.md).

Only **Signal Ingestion** consumes Lakehouse data via MCP in the demo case folders. Downstream agents run on workflow memory; their expected handoffs live in ground truth / `scenario.json`.

## Data generation (reference)

[`data-generation/`](data-generation/) holds source exports, entity catalogs, ground truth, expected outputs, and regeneration scripts — not needed to run a demo.

- [`data-generation/docs/HANDOFF.md`](data-generation/docs/HANDOFF.md) — full five-agent handoff map
- [`data-generation/docs/TEST_CASES.md`](data-generation/docs/TEST_CASES.md) — scenario index
- [`data-generation/scripts/scenarios.py`](data-generation/scripts/scenarios.py) — single source of truth for the 5 e2e scenarios

Regenerate demo cases:

```bash
cd data-generation/scripts
python3 generate_raw_layer.py
python3 build_case_folders.py         # writes dataset-seed/cases/
```

Optional: `python3 generate_normalized_layers.py` refreshes `ground-truth/` validation answer keys.

## Backend (Inventory Planning API)

ASP.NET Core API that runs the five-agent workflow against Azure AI Foundry.

| Path | Purpose |
|------|---------|
| `backend/Api.Host/` | API host (`http://localhost:5038`) |

### Start the backend (terminal 1)

```powershell
cd backend/Api.Host

$env:AZURE_FOUNDRY_PROJECT_ENDPOINT = "https://your-project.services.ai.azure.com/api/projects/your-project"
$env:Dataset__RootPath = "C:\cohere\inesite-agentic-inventory-planning\dataset-seed"

dotnet run --launch-profile http
```

Verify: `curl http://localhost:5038/health` → `{"status":"ok"}`

Copy [`backend/Api.Host/.env.local.example`](backend/Api.Host/.env.local.example) for all env var names.

## Frontend (Cohere.InventoryAndTrend)

Blazor Interactive Server app that calls the backend API. Requires the backend to be running.

| Path | Purpose |
|------|---------|
| `frontend/src/WebApp/` | Blazor application |
| `frontend/tests/WebApp.Tests/` | Unit tests |
| `frontend/src/WebApp/BACKEND_INTEGRATION.md` | Integration playbook |
| `frontend/UI_ENDPOINT_MAPPING.md` | UI action → endpoint map |

### Start the frontend (terminal 2)

```powershell
cd frontend/src/WebApp
dotnet run --launch-profile http
```

Open **http://localhost:5147** (backend default: **http://localhost:5038**).

The frontend reads case metadata from `dataset-seed/cases/catalog.json` and calls the backend for workflow execution. Override the backend URL if needed:

```powershell
$env:PlanningApi__BaseUrl = "http://localhost:5038/"
```

### Smoke test

1. Home → pick one of **5 cases** (`case-01` … `case-05`)
2. **Start planning run** → workspace opens
3. **Start workflow** → backend runs agents; UI polls every 2s
4. When complete → **Approve / Reject** at Planner Review (client-side gate)
5. Outcome summary appears

Run tests:

```powershell
dotnet test frontend/tests/WebApp.Tests/Cohere.InventoryAndTrend.WebApp.Tests.csproj
```

## Agent provisioning

See [agent-provisioning/README.md](agent-provisioning/README.md).
