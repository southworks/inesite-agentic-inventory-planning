# inesite-agentic-inventory-planning

Reference implementation of the "Retail inventory planning and trend forecasting" use case
(Signal Ingestion → Feature & Causality → Forecasting → Replenishment & Allocation →
Planner Copilot, powered by Grok 4.3 in Azure AI Foundry).

Reference user story: [US 128593](https://dev.azure.com/southworks/inesite/_workitems/edit/128593).

## Dataset seed (demo)

[`dataset-seed/`](dataset-seed/) holds **demo-ready inputs only**: five e2e cases, case-scoped prerequisite entities, and Fabric upload payloads. Each case is self-contained — pick one, upload ingest files, submit the orchestrator request.

```
dataset-seed/
  cases/case-01 … case-05/         README + ingest/ + fabric-pre-requisite-data/
    fabric-pre-requisite-data/      only the normalized entities referenced by that case
    ingest/                        POS, inventory, supplier, promotions
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
Agentic inventory planning and trend forecasting — agent provisioning, frontend demo, and workflow documentation.

## Frontend (Cohere.InventoryAndTrend)

Blazor Interactive Server app using **local dataset-seed mock data** (no backend required).

```powershell
cd frontend/src/WebApp
dotnet run
```

Open `http://localhost:5147` — pick a planning scenario, run the five-agent workflow, and approve at Planner Review.

| Path | Purpose |
|------|---------|
| `frontend/src/WebApp/` | Blazor application |
| `frontend/dataset-seed/` | Demo scenarios and canned agent outputs |
| `frontend/tests/WebApp.Tests/` | Unit tests |
| `frontend/src/WebApp/BACKEND_INTEGRATION.md` | Playbook for hooking up the real API |

Run tests:

```powershell
dotnet test frontend/tests/WebApp.Tests/
```

Switch to remote backend when ready: set `PlanningApi:Mode` to `Remote` in `appsettings.json` (see `BACKEND_INTEGRATION.md`).

## Agent provisioning

See [agent-provisioning/README.md](agent-provisioning/README.md).
