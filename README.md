# inesite-agentic-inventory-planning

Reference implementation of the "Retail inventory planning and trend forecasting" use case
(Signal Ingestion → Feature & Causality → Forecasting → Replenishment & Allocation →
Planner Copilot, powered by Grok 4.3 in Azure AI Foundry).

Reference user story: [US 128593](https://dev.azure.com/southworks/inesite/_workitems/edit/128593).

## Dataset seed

[`dataset-seed/`](dataset-seed/) holds the synthetic-but-coherent retail dataset the agent
pipeline consumes, built in the direction the agents actually consume it:

```
00_raw/
  _full_exports/  (canonical real M5-derived + synthetic system exports — single source of truth)
  IPF-XXX_<path>/ (5 end-to-end scenarios, each with a folder per agent/stage)
  → generate_normalized_layers.py reads _full_exports/ →
       01_pos_transactions/  02_supplier_data/  03_promotions/  04_inventory/  05_demand_signals/
  → 06_policy_rag/ (hand-authored policy docs) →
       07_decision_ground_truth/  (one e2e rollup per scenario)
  → build_scenario_folders.py materializes 00_raw/IPF-XXX_<path>/<stage>/
```

- [`dataset-seed/HANDOFF.md`](dataset-seed/HANDOFF.md) — the per-agent handoff map: what each of the 5 agents receives, produces, and passes on, and how to start a demo from any agent.
- [`dataset-seed/scenarios.py`](dataset-seed/scenarios.py) — the 5 end-to-end test-case scenarios (single source of truth, imported by both generators).
- [`dataset-seed/RAW_LAYER.md`](dataset-seed/RAW_LAYER.md) — source data, the e2e/per-agent Raw layer structure, and the signal log (seasonal trend, promotion spike, supplier delay, stockout risk, demand anomaly).
- [`dataset-seed/TEST_CASES.md`](dataset-seed/TEST_CASES.md) — index of the 5 e2e test cases and how to trace each through the agent chain to its Raw Layer files.
- [`dataset-seed/TESTING_GUIDE.md`](dataset-seed/TESTING_GUIDE.md) — high-level demo runbook: how to drive each scenario by injecting the prepared per-agent folders (no generation, no terminal), with a plain-language data-flow narrative and inject→observe→hand-off steps per scenario.
- [`dataset-seed/COMMON_JSON_FIELDS_SCHEMA.md`](dataset-seed/COMMON_JSON_FIELDS_SCHEMA.md) — fields shared by every normalized JSON document.
- Each `01_`–`07_` folder has its own `SCHEMA.md`.
- [`dataset-seed/dataset_summary.json`](dataset-seed/dataset_summary.json) — document counts and scenario coverage.
- [`dataset-seed/AGENT_INPUTS.md`](dataset-seed/AGENT_INPUTS.md) — PDF/PNG renderings of `00_raw/` for OCR/vision agent demos (66 files: weekly sales/inventory reports, supplier profiles, shipment receiving reports + packing slip scans, promo briefs).

Regenerate everything with:

```bash
cd dataset-seed
rm -rf 00_raw                          # layout changed; clear the old tree
python3 generate_raw_layer.py          # 00_raw/_full_exports/ canonical exports
pip install -r requirements.txt
python3 generate_agent_documents.py    # pdf/png renderings into _full_exports/
python3 generate_normalized_layers.py  # 01-05 + 07 e2e rollups from _full_exports/
python3 build_scenario_folders.py      # 00_raw/IPF-XXX_<path>/<stage>/ per-agent folders
```