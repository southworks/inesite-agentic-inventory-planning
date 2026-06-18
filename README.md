# inesite-agentic-inventory-planning

Reference implementation of the "Retail inventory planning and trend forecasting" use case
(Signal Ingestion → Feature & Causality → Forecasting → Replenishment & Allocation →
Planner Copilot, powered by Grok 4.3 in Azure AI Foundry).

Reference user story: [US 128593](https://dev.azure.com/southworks/inesite/_workitems/edit/128593).

## Dataset seed

[`dataset-seed/`](dataset-seed/) holds the synthetic-but-coherent retail dataset the agent
pipeline consumes, built in the direction the agents actually consume it:

```
00_raw/ (organized by scenario / test case)
  _full_exports/  (canonical real M5-derived + synthetic system exports — single source of truth)
  <SCENARIO-ID>/  (self-contained per-scenario slices, 10 folders)
  → generate_normalized_layers.py reads _full_exports/ →
       01_pos_transactions/  02_supplier_data/  03_promotions/  04_inventory/  05_demand_signals/
  → 06_policy_rag/ (hand-authored policy docs) →
       07_decision_ground_truth/
```

- [`dataset-seed/RAW_LAYER.md`](dataset-seed/RAW_LAYER.md) — source data, the scenario-first Raw layer structure, and the 10-scenario log (2 each: seasonal trend, promotion spike, supplier delay, stockout risk, demand anomaly).
- [`dataset-seed/TEST_CASES.md`](dataset-seed/TEST_CASES.md) — index of the 10 test cases and how to trace each to its Raw Layer folder.
- [`dataset-seed/COMMON_JSON_FIELDS_SCHEMA.md`](dataset-seed/COMMON_JSON_FIELDS_SCHEMA.md) — fields shared by every normalized JSON document.
- Each `01_`–`07_` folder has its own `SCHEMA.md`.
- [`dataset-seed/dataset_summary.json`](dataset-seed/dataset_summary.json) — document counts and scenario coverage.
- [`dataset-seed/AGENT_INPUTS.md`](dataset-seed/AGENT_INPUTS.md) — PDF/PNG renderings of `00_raw/` for OCR/vision agent demos (66 files: weekly sales/inventory reports, supplier profiles, shipment receiving reports + packing slip scans, promo briefs).

Regenerate everything with:

```bash
cd dataset-seed
rm -rf 00_raw                          # layout changed; clear the old tree
python3 generate_raw_layer.py          # 00_raw/_full_exports/ + per-scenario slices
python3 generate_normalized_layers.py  # 01-05 + 07 from 00_raw/_full_exports/
pip install -r requirements.txt
python3 generate_agent_documents.py    # pdf/png renderings + per-scenario copies
```