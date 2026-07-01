# Retail Data Generation

Reference material for building and validating the retail inventory-planning dataset. Not required to run a demo — use [`../dataset-seed/`](../dataset-seed/) for that.

## Layout

```
data-generation/
  corpus/                    m5_extract.json + canonical csv/txt exports (POS, supplier, promo, inventory)
  ground-truth/              optional e2e answer keys (IPF-XXX.json) for validation
  scripts/                   generators and build_case_folders.py
  docs/                      handoff, testing guide, schemas
```

## Regenerate demo cases

```bash
cd data-generation/scripts
python3 generate_raw_layer.py       # corpus/ csv|txt exports — only when corpus signals change
python3 build_case_folders.py       # dataset-seed/cases/*/ingest/ + fabric-pre-requisite-data/
python3 build_case_folders.py --scenario IPF-XXX   # single scenario only (recommended for new cases)
```

Optional:

```bash
pip install -r requirements.txt
python3 generate_agent_documents.py        # pdf/png into corpus/
python3 generate_normalized_layers.py      # refresh ground-truth/ rollups + ground_truth.csv
```

`generate_normalized_layers.py` computes expected outputs from the corpus and `scenarios.py`; it is optional for runtime demo cases but recommended when adding or changing scenarios.

Demo data lands directly in `dataset-seed/cases/`.

## How runtime discovers scenarios

Regenerating `dataset-seed/` is necessary but not sufficient — new cases must also be wired in the UI and API.

| Layer | Behavior |
|-------|----------|
| **UI** | `BackendCaseCatalogService` reads `dataset-seed/cases/catalog.json` for the Home / scenario picker. Add an entry per new case (title, description, `outcomeTag`, `context`). `build_case_folders.py` does **not** write this file. |
| **API** | `LocalDocumentStorageService` uses a hardcoded `SupportedCaseIds` set. The workflow start endpoint rejects ids not in that set even if `ingest/` exists. |
| **Case folders** | `case-01` … `case-NN`, mapped from `CASE_FOLDERS` in `scenarios.py`. |

There is no auto-sync from `scenarios.py` or `catalog.json` into the C# allow-list — both wiring steps are required for a new case to work end-to-end.

## How to add a scenario

New scenarios are written into `dataset-seed/`. They do **not** appear in a running app until you rebuild the generated assets, republish container images or deployment packages, and redeploy.

### 1. Plan the scenario

- Pick a **scenario type** (`seasonal_trend`, `promotion_demand_spike`, `stockout_risk`, `demand_anomaly`) or extend `compute_decision()` in `generate_normalized_layers.py` for a new type.
- Choose the next legacy id (`IPF-XXX`) and demo folder (`case-XX`). Add the mapping in `CASE_FOLDERS` in `scenarios.py`.
- The canonical corpus covers **6 SKUs × 2 stores** from `corpus/m5_extract.json`. New products or stores require extending the extract and `generate_raw_layer.py`.

### 2. Add or reuse corpus signals

| Need | Action |
|------|--------|
| Reuse existing POS/promo/shipment signals | Skip `generate_raw_layer.py`; only edit `scenarios.py` and run `build_case_folders.py --scenario IPF-XXX` |
| New promo, anomaly, shipment, or inventory override | Edit constants in `scripts/generate_raw_layer.py` (`PROMO_EVENTS`, `ANOMALIES`, `SUPPLIER_SHIPMENTS`, `STOCKOUT_WINDOWS`), then run `generate_raw_layer.py` |

Each scenario declares a **`raw_slice`** in `scenarios.py` (which SKUs, stores, suppliers, promos, and source types to copy into `ingest/`).

### 3. Declare the scenario

Add an entry to `SCENARIOS` in `scripts/scenarios.py` using `_make()`:

- `scenario_id`, `path`, `title`, `final_outcome`, `required_human_review`
- `anchor` — sku, stores, weeks, `type`, policy refs, summary (used by ground-truth computation)
- `raw_slice` — skus, stores, suppliers, shipments, promo_events, sources
- `orchestrator_request`, stage flags (`has_promo`, `anomaly`, `planner_hitl`)

Numeric expected outputs are **computed** by `generate_normalized_layers.py`, not hand-written.

### 4. Regenerate derived assets

```bash
cd data-generation/scripts
# If corpus signals changed:
python3 generate_raw_layer.py

# Build only the new case (avoids rewriting existing case folders):
python3 build_case_folders.py --scenario IPF-XXX

# Refresh validation answer keys:
python3 generate_normalized_layers.py
```

### 5. Wire runtime and UI (required)

Regenerating `dataset-seed/` alone is not sufficient — see [How runtime discovers scenarios](#how-runtime-discovers-scenarios). Do both:

**API** — add `case-XX` to `SupportedCaseIds` in `backend/GrokInventoryAndTrend.Api/Services/LocalDocumentStorageService.cs`. Update supported-case error messages in `InventoryPlanningController.cs` and `InventoryPlanningWorkflowService.cs` if they enumerate case ids.

**UI** — add an entry to `../dataset-seed/cases/catalog.json` (title, description, `outcomeTag`, `context`).

### 6. Review output

Check before committing:

- `../dataset-seed/cases/case-XX/` — `ingest/`, `fabric-pre-requisite-data/`
- `../dataset-seed/cases/catalog.json`
- `ground-truth/IPF-XXX.json`, `ground-truth/ground_truth.csv`
- `scripts/dataset_summary.json`

### 7. Rebuild and redeploy

Rebuild any image or deployment package that embeds `dataset-seed/`, then redeploy.

### Tips

- Prefer `build_case_folders.py --scenario IPF-XXX` when adding a variant so existing `case-01` … `case-NN` ingest files are not rewritten.
- Run `generate_raw_layer.py` only when you change synthetic signals in the canonical corpus.
- One SKU per scenario is the current model; multi-SKU allocation is not supported without extending the scripts and agents.

## Key docs

- [`docs/HANDOFF.md`](docs/HANDOFF.md) — five-agent handoff map
- [`docs/TESTING_GUIDE.md`](docs/TESTING_GUIDE.md) — Fabric upload + e2e demo runbook
- [`docs/TEST_CASES.md`](docs/TEST_CASES.md) — scenario index
- [`docs/TEAM_HANDOFF.md`](docs/TEAM_HANDOFF.md) — structural change summary for integration teams
