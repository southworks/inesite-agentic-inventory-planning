# Retail Demo Dataset

Demo-ready inputs for the Agentic Inventory Planning workflow. Pick a case, upload ingest files to Fabric, and submit the planning request.

## Quick start

1. **Pick a case** under `cases/` (each is a standalone e2e scenario)
2. **Upload** the files in `ingest/` to Fabric (Lakehouse)

## Cases 1–5 (e2e scenarios)

| Case | Folder | Legacy ID | Ingest | Expected outcome |
|------|--------|-----------|--------|------------------|
| Case 1 | `cases/case-01-seasonal-happy-path/` | IPF-001 | Yes — signal ingestion | Clean holiday forecast; order approved (208 units) |
| Case 2 | `cases/case-02-promotion-budget-review/` | IPF-002 | Yes — signal ingestion | Order within budget (584 units); planner budget HITL |
| Case 3 | `cases/case-03-supplier-delay-expedite/` | IPF-003 | Yes — signal ingestion | Expedite required; planner service-level HITL |
| Case 4 | `cases/case-04-partial-fill-reorder/` | IPF-004 | Yes — signal ingestion | Reorder approved (80 units = MOQ) |
| Case 5 | `cases/case-05-demand-anomaly/` | IPF-005 | Yes — signal ingestion | Anomaly flagged; no supply order; forecasting HITL |

Each case folder contains:

- `README.md` — user action, ingest summary, expected outcome, legacy ID
- `fabric-pre-requisite-data/` — case-scoped normalized entities referenced by the scenario
- `ingest/` — flat POS, inventory, supplier, and promotion files for Fabric upload

## Reference material

Generation scripts, source exports, entity catalogs, expected outputs, and ground truth live in [`../data-generation/`](../data-generation/). Regenerate demo data with `build_case_folders.py` (see that README). Legacy scenario IDs (`IPF-001` … `IPF-005`) are preserved in ground truth for validation.

## Team note (structural change)

Demo folders were renamed from `00_raw/IPF-XXX_<path>/` to **Case 1–5** under `cases/`. Backend was not modified in this repo — update any hardcoded paths that pointed at `dataset-seed/00_raw/` or old scenario folder names.
