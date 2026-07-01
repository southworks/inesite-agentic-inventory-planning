# Retail Demo Dataset

Demo-ready inputs for the Agentic Inventory Planning workflow. Pick a case and submit the planning request.

## Quick start

1. **Pick a case** — `case-01` through `case-05` (see `cases/catalog.json`)
2. **Start workflow** — `POST /api/inventory-planning/cases/{caseId}/workflow/basic/start`
3. **MCP data** — case-scoped normalized JSON lives in `cases/{caseId}/fabric-pre-requisite-data/`

## Cases 1–5 (e2e scenarios)

| Case | Folder | Legacy ID | Ingest | Expected outcome |
|------|--------|-----------|--------|------------------|
| Case 1 | `cases/case-01/` | IPF-001 | Yes — signal ingestion | Clean holiday forecast; order approved (208 units) |
| Case 2 | `cases/case-02/` | IPF-002 | Yes — signal ingestion | Order within budget (584 units); planner budget HITL |
| Case 3 | `cases/case-03/` | IPF-003 | Yes — signal ingestion | Expedite required; planner service-level HITL |
| Case 4 | `cases/case-04/` | IPF-004 | Yes — signal ingestion | Reorder approved (80 units = MOQ) |
| Case 5 | `cases/case-05/` | IPF-005 | Yes — signal ingestion | Anomaly flagged; no supply order; forecasting HITL |

Each case folder contains:

- `README.md` — user action, ingest summary, expected outcome, legacy ID
- `fabric-pre-requisite-data/` — case-scoped normalized entities referenced by the scenario
- `ingest/` — flat POS, inventory, supplier, and promotion files exposed by API document endpoints

## Reference material

Generation scripts, source exports, entity catalogs, expected outputs, and ground truth live in [`../data-generation/`](../data-generation/). Regenerate demo data with `build_case_folders.py` (see that README). Legacy scenario IDs (`IPF-001` … `IPF-005`) are preserved in ground truth for validation.

## How to add a scenario

Add or modify scenarios in [`../data-generation/`](../data-generation/), not directly in this runtime package. After updating `data-generation/scripts/scenarios.py`, regenerate `dataset-seed/`, add the new `case-XX` to the `SupportedCaseIds` HashSet in `backend/GrokInventoryAndTrend.Api/Services/LocalDocumentStorageService.cs`, rebuild the images or deployment package that embeds this folder, and redeploy. The API does not dynamically ingest new cases at runtime.
