# Agent Handoff — what each agent receives, produces, and passes on

This document is the precise handoff map between the agents in the
*Retail – Agentic inventory planning & trend forecasting* workflow and the dataset entities
that carry the data across the chain. It is the bridge between the proposal diagram and
`dataset-seed/`.

See [TEST_CASES.md](TEST_CASES.md), [RAW_LAYER.md](RAW_LAYER.md), and
[SCENARIO_ORGANIZATION.md](SCENARIO_ORGANIZATION.md).

## System objective

An agentic inventory-planning hub that **ingests** retail system-of-record signals (POS,
supplier, promotions, inventory), **engineers features and tests causal drivers**, produces a
**short-term demand forecast**, and turns it into **replenishment / allocation orders** that
respect service-level and budget policy. The **Orchestrator – Planning agent** routes an
(optional) planning request to the sub-agents; each ground-truth case is a full e2e pass
through all five agents.

## What lives under `00_raw/` (source of truth for demos)

Each scenario folder `00_raw/IPF-XXX_<path>/` materializes **only the stages whose agents
consume data via MCP tools**. The `input/` subfolders hold the **payload to upload** to the
data platform (Microsoft Fabric Lakehouse in demos); agents query that storage at runtime, not
the repo path directly.

```
00_raw/IPF-XXX_<path>/
  01_signal_ingestion/
    agent_input.json
    input/              ← upload to Fabric: raw csv/txt + marquee pdf/png (systems of record)
    expected_output/    ← validation only: normalized POS/INV/SUP/PROMO + _expected_output.json
  02_forecasting/
    agent_input.json
    input/              ← upload to Fabric: scoped DMD-{sku}.json + seasonal_planning_policy.txt
    expected_output/    ← validation only: forecast_result.json + _expected_output.json
  scenario.json         ← mirror of 07_decision_ground_truth/IPF-XXX.json (full e2e answer key)
```

**Prerequisite for testing:** before running a scenario, the `input/` files for that scenario
must be present in Fabric (or your MCP-backed store) and reachable by the agent tools. See
[TESTING_GUIDE.md](TESTING_GUIDE.md#prerequisites--data-in-fabric-before-you-run-a-scenario).

The orchestrator's planning request is **`orchestrator_request`** inside `scenario.json` (there
is no separate `01_orchestrator/` folder). Feature & Causality, Replenishment, and Planner
Copilot do **not** have folders under `00_raw/` — they run on workflow memory in a full demo;
their `agent_input`, `decision`, and `expected_output` are in `scenario.json` → `stages[]`.

The normalized catalog folders `01_pos_transactions/` … `05_demand_signals/` at the
`dataset-seed/` root are **build inputs** used by `build_scenario_folders.py` to populate
`expected_output/` and the forecasting `DMD` — agents do not read them directly at runtime.

## Handoff chain (full workflow)

| # | Agent | Receives | Consumes (entities) | Produces | Hands off to | HITL gate | Materialized in `00_raw/`? |
|---|---|---|---|---|---|---|---|
| 0 | **Orchestrator** | optional planning request | — | routed workflow | Signal Ingestion | — | `scenario.json` → `orchestrator_request` only |
| 1 | **Signal Ingestion** | `sources` + `scope` + `window` | raw exports in `input/` | normalized `01`–`04` entities | Feature & Causality | Validate quality | **yes** — `01_signal_ingestion/` |
| 2 | **Feature & Causality** | validated signals | `01`–`04` + calendar | `05_demand_signals` (DMD) | Forecasting | — | no — `stages[]` in `scenario.json`; DMD precalculated in `02_forecasting/input/` |
| 3 | **Forecasting** | scope + causality summary | `05` + `seasonal_planning_policy.txt` | `forecast_result.json` | Replenishment | Short-term trend (anomaly path) | **yes** — `02_forecasting/` |
| 4 | **Replenishment & Allocation** | `forecast_result.json` | `04_inventory`, `02_supplier_data` | `replenishment_plan.json` | Planner Copilot | — | no — `stages[]` in `scenario.json` |
| 5 | **Planner Copilot** | `replenishment_plan.json` | `06_policy_rag` (SL, BG) | `planner_decision.json` | user | Enforce budget / service-level | no — `stages[]` in `scenario.json` |

**Human-in-the-loop:** Validate quality (Signal Ingestion), Short-term trend (Forecasting,
anomaly path only), Enforce budget / service-level (Planner Copilot) — each carried by the
stage `gate` field and `required_human_review` on the scenario rollup.

## End-to-end scenarios (the test cases)

Defined in [`scenarios.py`](scenarios.py), see [TEST_CASES.md](TEST_CASES.md):

| Scenario | Path | SKU @ store | Drives | Final outcome | HITL |
| --- | --- | --- | --- | --- | --- |
| `IPF-001` | seasonal happy path | `HOUSEHOLD_1_334` @ TX_2 | holiday demand peak (real) | `order_approved` (208 units) | none |
| `IPF-002` | promotion spike → budget gate | `FOODS_3_252` @ TX_2 | promo uplift (SN-510) | `order_approved_within_budget` (584 units) | budget review |
| `IPF-003` | supplier delay → stockout → expedite | `HOUSEHOLD_1_447` @ TX_2 | 14-day late shipment, on-hand→0 | `expedite_required` (qty 0, in-transit covers) | service-level |
| `IPF-004` | partial fill → stockout → reorder | `HOBBIES_1_048` @ CA_1 | 57.9%-fill shipment, shortfall | `reorder_approved` (80 = MOQ) | none |
| `IPF-005` | demand anomaly → no action | `HOBBIES_1_268` @ CA_1 | unexplained dip, no driver | `flagged_anomaly_no_action` (qty 0) | anomaly review |

## Diagram support blocks → dataset

- **RAG — Validate quality (Signal Ingestion):** raw documents in `01_signal_ingestion/input/`
  (uploaded to Fabric; queried via MCP).
- **RAG — Short-term trend (Forecasting):** `seasonal_planning_policy.txt` in
  `02_forecasting/input/` (SN-500, SN-510) — same Fabric prerequisite.
- **Data / systems of record:** sliced from `00_raw/_full_exports/` into each scenario's
  `input/` folders, then uploaded to the Lakehouse for MCP access.
- **Governance & eval:** `07_decision_ground_truth/` and each scenario's `scenario.json`
  (local — not uploaded for agent consumption).

## How to run a demo

**Full e2e (recommended):** ensure the scenario's `input/` data is in Fabric; pass
`scenario.json` → `orchestrator_request` to the orchestrator; run all five agents in order.
MCP tools read from the Lakehouse at the two data stages:

```bash
# Prerequisite — upload to Fabric (example paths; adjust to your Lakehouse layout)
#   00_raw/IPF-001_seasonal_happy_path/01_signal_ingestion/input/**
#   00_raw/IPF-001_seasonal_happy_path/02_forecasting/input/**

# IPF-001 — trigger (local answer key)
jq '.orchestrator_request' 00_raw/IPF-001_seasonal_happy_path/scenario.json

# Step 1 — Signal Ingestion (MCP reads from Fabric; agent_input local)
cat 00_raw/IPF-001_seasonal_happy_path/01_signal_ingestion/agent_input.json

# Step 3 — Forecasting (MCP reads from Fabric; agent_input local)
cat 00_raw/IPF-001_seasonal_happy_path/02_forecasting/agent_input.json

# Answer key for every stage
jq '.stages[] | {stage, decision, expected_output}' 00_raw/IPF-001_seasonal_happy_path/scenario.json
```

**Single-agent isolation:** upload one stage's `input/` to Fabric; run that agent with MCP;
compare against that stage's `expected_output/`. For Forecasting, the `DMD` in `input/` stands in for
Feature & Causality having already run.

## Notes

- **The budget cap (BG-300, 3× avg weekly demand) does not bind on any seed case** — the
  largest order (IPF-002, 584 units) sits well under its 1095-unit cap. IPF-002 still routes
  through the Planner budget gate for human review.
- Deliberately **not** modeled as a normalized ERP layer: multi-warehouse allocation splits.
  `replenishment_plan.json` in `scenario.json` is the explicit handoff artifact for the
  proposed order/expedite decision.
