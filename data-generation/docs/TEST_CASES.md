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

New scenarios are generated into `dataset-seed/` and only affect the running app after the generated assets are rebuilt, container images or deployment packages are republished, and Azure is redeployed.

1. Add or update the source signal in `data-generation/corpus/` via `generate_raw_layer.py`.
2. Add the `IPF-XXX` scenario and its `CASE_FOLDERS` mapping in `data-generation/scripts/scenarios.py`.
3. Regenerate with:
   ```bash
   cd data-generation/scripts
   python3 generate_raw_layer.py
   python3 build_case_folders.py
   ```
   Run `generate_normalized_layers.py` only when refreshing validation answer keys and the intermediate normalized catalog is present.
4. Review `dataset-seed/cases/{caseId}/`, `dataset-seed/cases/catalog.json`, and `data-generation/ground-truth/`.
5. Add the new backend case id to `SupportedCaseIds` in `backend/GrokInventoryAndTrend.Api/Services/LocalDocumentStorageService.cs`.
6. Rebuild and redeploy the images or deployment package that embeds `dataset-seed/`.
