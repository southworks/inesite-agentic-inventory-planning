# Test Cases

Scenario definitions live in [`../scripts/scenarios.py`](../scripts/scenarios.py). Demo folders are built by [`../scripts/build_case_folders.py`](../scripts/build_case_folders.py).

## Scenario index

| Case id | Legacy ID | Path | Final outcome | Runtime folder |
| --- | --- | --- | --- | --- |
| `case-01` | `IPF-001` | `seasonal_happy_path` | `order_approved` | `dataset-seed/cases/case-01/` |
| `case-02` | `IPF-002` | `promotion_spike_budget_review` | `order_approved_within_budget` | `dataset-seed/cases/case-02/` |
| `case-03` | `IPF-003` | `supplier_delay_stockout_expedite` | `expedite_required` | `dataset-seed/cases/case-03/` |
| `case-04` | `IPF-004` | `partial_fill_stockout_reorder` | `reorder_approved` | `dataset-seed/cases/case-04/` |
| `case-05` | `IPF-005` | `demand_anomaly_no_action` | `flagged_anomaly_no_action` | `dataset-seed/cases/case-05/` |

Each runtime folder contains `ingest/` for API document listing and `fabric-pre-requisite-data/` for MCP case-scoped data. Case metadata for the UI lives in `dataset-seed/cases/catalog.json`.

## Ground truth

Optional validation rollups live in `data-generation/ground-truth/IPF-XXX.json` and `ground_truth.csv`. These are reference answer keys; runtime agents read from the bundled `dataset-seed/` package.

## How to add a scenario

See [`../README.md`](../README.md#how-to-add-a-scenario).
