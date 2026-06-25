# Retail Demo Dataset

Demo-ready inputs for the Agentic Inventory Planning workflow. Pick a case, load policies, upload ingest files to Fabric, and submit the planning request.

## Quick start

1. **Pick a case** under `cases/` (each is a standalone e2e scenario)
2. **Load** `policies/retail_policies.txt` into your RAG / embed pipeline
3. **Upload** `ingest/signal_ingestion/` then `ingest/forecasting/` to Fabric (Lakehouse)
4. **Send** the content of `user_input.txt` as the orchestrator trigger

## Cases 1–5 (e2e scenarios)

| Case | Folder | Legacy ID | Ingest | Expected outcome |
|------|--------|-----------|--------|------------------|
| Case 1 | `cases/case-01-seasonal-happy-path/` | IPF-001 | Yes — dual stage | Clean holiday forecast; order approved (208 units) |
| Case 2 | `cases/case-02-promotion-budget-review/` | IPF-002 | Yes — dual stage | Order within budget (584 units); planner budget HITL |
| Case 3 | `cases/case-03-supplier-delay-expedite/` | IPF-003 | Yes — dual stage | Expedite required; planner service-level HITL |
| Case 4 | `cases/case-04-partial-fill-reorder/` | IPF-004 | Yes — dual stage | Reorder approved (80 units = MOQ) |
| Case 5 | `cases/case-05-demand-anomaly/` | IPF-005 | Yes — dual stage | Anomaly flagged; no supply order; forecasting HITL |

Each case folder contains:

- `README.md` — user action, ingest summary, expected outcome, legacy ID
- `user_input.txt` — preset orchestrator planning request
- `ingest/signal_ingestion/` — POS, inventory, supplier, promotions (Fabric upload stage 1)
- `ingest/forecasting/` — scoped `DMD-{sku}.json` + `seasonal_planning_policy.txt` (Fabric upload stage 2)

## Policies

All governance rules for the demo are in [`policies/retail_policies.txt`](policies/retail_policies.txt) — one file, ready to embed/load.

## Reference material

Generation scripts, source exports, entity catalogs, expected outputs, and ground truth live in [`../data-generation/`](../data-generation/). Legacy scenario IDs (`IPF-001` … `IPF-005`) are preserved there for validation and rebuild.

## Team note (structural change)

Demo folders were renamed from `00_raw/IPF-XXX_<path>/` to **Case 1–5** under `cases/`. Backend was not modified in this repo — update any hardcoded paths that pointed at `dataset-seed/00_raw/` or old scenario folder names.
