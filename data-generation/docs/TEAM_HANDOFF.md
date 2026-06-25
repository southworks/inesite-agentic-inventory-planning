# Team handoff — Retail dataset structure change

**Date:** 2025-06-25  
**Repo:** `inesite-agentic-inventory-planning`  
**Backend:** not modified in this repo

## What changed

| Before | After |
|--------|-------|
| `dataset-seed/00_raw/` (exports + scenarios mixed in) | Removed from demo package |
| `dataset-seed/01_*` … `07_*` entity catalogs | `data-generation/entity-catalog/` |
| `expected-outputs/IPF-XXX_<path>/` scenario folders | `dataset-seed/cases/case-01` … `case-05` |
| Policies in `06_policy_rag/*.txt` (scattered) | Single `dataset-seed/policies/retail_policies.txt` |
| Scripts in `dataset-seed/` | `data-generation/scripts/` |

## Legacy IDs preserved

`IPF-001` … `IPF-005` remain in:

- Each case `README.md` under `dataset-seed/`
- `data-generation/ground-truth/07_decision_ground_truth/*.json`
- `data-generation/scripts/scenarios.py`
- `data-generation/expected-outputs/IPF-XXX_<path>/`

## Action for integration teams

If any runtime code pointed at old paths (`dataset-seed/00_raw/`, `IPF-XXX_<path>/` under dataset-seed, etc.), update to the new demo layout documented in [`dataset-seed/README.md`](../../dataset-seed/README.md).

## Case mapping

| Case | Legacy ID |
|------|-----------|
| Case 1 — seasonal happy path | IPF-001 |
| Case 2 — promotion budget review | IPF-002 |
| Case 3 — supplier delay expedite | IPF-003 |
| Case 4 — partial fill reorder | IPF-004 |
| Case 5 — demand anomaly | IPF-005 |
