# inesite-agentic-inventory-planning

Reference implementation of the "Retail inventory planning and trend forecasting" use case
(Signal Ingestion → Feature & Causality → Forecasting → Replenishment & Allocation →
Planner Copilot, powered by Grok 4.3 in Azure AI Foundry).

Reference user story: [US 128593](https://dev.azure.com/southworks/inesite/_workitems/edit/128593).

## Dataset seed

[`dataset-seed/`](dataset-seed/) holds the synthetic-but-coherent retail dataset for the
workflow. **`00_raw/` is the source of truth** for what each scenario contains: files under
`input/` are the **upload payload** for Microsoft Fabric (Lakehouse); agents **query that
storage via MCP tools** at demo time — they do not read the repo directly.

```
00_raw/                              ← SOURCE OF TRUTH (content + answer keys)
  _full_exports/                     canonical csv/txt + pdf/png (generation input)
  IPF-XXX_<path>/                    one folder per e2e scenario
    01_signal_ingestion/             agent_input.json  input/ → Fabric  expected_output/
    02_forecasting/                  agent_input.json  input/ → Fabric  expected_output/
    scenario.json                    full e2e answer key (all five agents)

Build / eval layers (not uploaded for MCP consumption):
  01_pos_transactions/ … 05_demand_signals/   ← normalized catalog (feeds build_scenario_folders.py)
  06_policy_rag/                                ← policy corpus (SN-500 copied into forecasting input)
  07_decision_ground_truth/                   ← eval rollups (feeds scenario.json)
```

**Prerequisite:** upload each scenario's `01_signal_ingestion/input/` and `02_forecasting/input/`
to Fabric before testing. See [`dataset-seed/TESTING_GUIDE.md`](dataset-seed/TESTING_GUIDE.md).

Only **Signal Ingestion** and **Forecasting** consume that Lakehouse data via MCP. The full
five-agent chain is demonstrated end-to-end; downstream agents (Feature & Causality,
Replenishment, Planner Copilot) run on **workflow memory** — their expected handoffs live in
`scenario.json` / `07_decision_ground_truth/`.

- [`dataset-seed/HANDOFF.md`](dataset-seed/HANDOFF.md) — full five-agent handoff map; what is materialized under `00_raw/` vs. workflow-only.
- [`dataset-seed/scenarios.py`](dataset-seed/scenarios.py) — the 5 e2e scenarios (single source of truth for generators).
- [`dataset-seed/RAW_LAYER.md`](dataset-seed/RAW_LAYER.md) — source data, `00_raw/` layout, and signal log.
- [`dataset-seed/TEST_CASES.md`](dataset-seed/TEST_CASES.md) — case index and how to trace each scenario.
- [`dataset-seed/TESTING_GUIDE.md`](dataset-seed/TESTING_GUIDE.md) — demo runbook: Fabric upload prerequisite + full e2e flow.
- [`dataset-seed/SCENARIO_ORGANIZATION.md`](dataset-seed/SCENARIO_ORGANIZATION.md) — narrative stories A–E per scenario.
- [`dataset-seed/COMMON_JSON_FIELDS_SCHEMA.md`](dataset-seed/COMMON_JSON_FIELDS_SCHEMA.md) — fields shared by every normalized JSON document.
- Each `01_`–`07_` folder has its own `SCHEMA.md` (build/eval layers).
- [`dataset-seed/dataset_summary.json`](dataset-seed/dataset_summary.json) — document counts and scenario coverage.
- [`dataset-seed/AGENT_INPUTS.md`](dataset-seed/AGENT_INPUTS.md) — PDF/PNG renderings under `_full_exports/` for OCR/vision demos.

Regenerate everything with:

```bash
cd dataset-seed
rm -rf 00_raw
python3 generate_raw_layer.py          # 00_raw/_full_exports/ canonical exports
pip install -r requirements.txt
python3 generate_agent_documents.py    # pdf/png renderings into _full_exports/
python3 generate_normalized_layers.py  # 01-05 + 07 e2e rollups from _full_exports/
python3 build_scenario_folders.py      # 00_raw/IPF-XXX_<path>/ (01_signal_ingestion + 02_forecasting)
```
