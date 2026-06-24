# Agent Handoff — what each agent receives, produces, and passes on

This document is the precise handoff map between the agents in the
*Retail – Agentic inventory planning & trend forecasting* workflow and the dataset entities
that carry the data across the chain. It is the bridge between the proposal diagram and
`dataset-seed/`.

It is validated against the proposal workflow and the `loan-mortgage-agents` reference
convention. See [TEST_CASES.md](TEST_CASES.md), [RAW_LAYER.md](RAW_LAYER.md), and
[SCENARIO_ORGANIZATION.md](SCENARIO_ORGANIZATION.md).

## System objective

An agentic inventory-planning hub that **ingests** retail system-of-record signals (POS,
supplier, promotions, inventory), **engineers features and tests causal drivers**, produces a
**short-term demand forecast**, and turns it into **replenishment / allocation orders** that
respect service-level and budget policy — every recommendation traceable back to a Raw Layer
signal. The **Orchestrator – Planning agent** routes an (optional) planning request to the
sub-agents; each ground-truth case is a full e2e pass, and each stage folder is a per-agent
entry point.

## Handoff chain

| # | Agent | Receives (`agent_input`) | Consumes (entities) | Produces (entities / decision) | Hands off to | HITL gate |
|---|---|---|---|---|---|---|
| 1 | **Signal Ingestion** | `sources` + `scope` + `window` | `00_raw/_full_exports/**` (POS, supplier, promo, inventory) | `01_pos_transactions`, `02_supplier_data`, `03_promotions`, `04_inventory` | Feature & Causality | Validate quality (passes for every seed case) |
| 2 | **Feature & Causality** | `scope` + `predictors` + `events` + `test_elasticity` | `01`–`04` + calendar | `05_demand_signals` (rolling avg, pct-change, promo/holiday/anomaly weeks) + `observed_uplift_pct` in `03` | Forecasting | — |
| 3 | **Forecasting** | `scope` + `horizon` | `05_demand_signals` | `forecast_result.json` (`expected_forecast_units_per_week` + `anomaly_flag`) | Replenishment & Allocation | Short-term trend — **only the unexplained-anomaly path** is routed to a human |
| 4 | **Replenishment & Allocation** | `scope` + `forecast_result.json` | `04_inventory`, `02_supplier_data` | `replenishment_plan.json` (`proposed_order_qty`, `shortfall_units`, `target_on_hand_units`, `expedite_required`) | Planner Copilot | — |
| 5 | **Planner Copilot** | `replenishment_plan.json` + `policy_refs` | `06_policy_rag` (SL-100, BG-300) | `planner_decision.json` (`approved_order_qty`, `binding_constraint`, `final_outcome`, `required_human_review`) | user | Enforce service-level / budget |

**Human-in-the-loop:** the diagram's three HITL points (Validate quality, Short-term trend,
Enforce budget) are carried by each stage's `gate` field and the scenario-level
`required_human_review`. The orchestrator's optional *Planning request* is modeled as
`01_orchestrator/request.json` per scenario.

## End-to-end scenarios (the test cases)

The handoff is exercised by five **e2e scenarios** (one full pass through all agents each),
which differ at the gates and at the supply/demand signal that drives the outcome. Defined in
[`scenarios.py`](scenarios.py), see [TEST_CASES.md](TEST_CASES.md):

| Scenario | Path | SKU @ store | Drives | Final outcome | HITL |
| --- | --- | --- | --- | --- | --- |
| `IPF-001` | seasonal happy path | `HOUSEHOLD_1_334` @ TX_2 | holiday demand peak (real) | `order_approved` (208 units) | none |
| `IPF-002` | promotion spike → budget gate | `FOODS_3_252` @ TX_2 | promo uplift (SN-510) | `order_approved_within_budget` (584 units) | budget review |
| `IPF-003` | supplier delay → stockout → expedite | `HOUSEHOLD_1_447` @ TX_2 | 14-day late shipment, on-hand→0 | `expedite_required` (qty 0, in-transit covers) | service-level |
| `IPF-004` | partial fill → stockout → reorder | `HOBBIES_1_048` @ CA_1 | 57.9%-fill shipment, shortfall | `reorder_approved` (80 = MOQ) | none |
| `IPF-005` | demand anomaly → no action | `HOBBIES_1_268` @ CA_1 | unexplained dip, no driver | `flagged_anomaly_no_action` (qty 0) | anomaly review |

## Diagram support blocks → dataset

- **Retrieval Tool Components** (Cohere Embed → Vector DB → Cohere Rerank → Top-N): the
  Planner Copilot and Forecasting agents retrieve `06_policy_rag/` (SL/RP/BG/SP/SN refs).
- **Data / systems of record**: POS & transactions → `01`; Supplier data → `02`; Promotions &
  price calendar → `03`; Inventory → `04` — all sourced from `00_raw/_full_exports/`.
- **Governance & resp. AI**: Evaluations → `07_decision_ground_truth` (this IS the eval
  harness — the e2e rollup scores every stage's expected output); Safety & compliance →
  `06_policy_rag` (SL-100 service level, BG-300 budget, SP-400/410 supplier).

## Start the demo from any agent

Each scenario's stage folder is self-contained, so a demo can begin mid-chain "as if the
previous agents had run". Under `00_raw/IPF-XXX_<path>/<stage>/`:

- `agent_input.json` — the structured payload to **start** that agent in isolation.
- `input/` — the documents it starts from (raw exports + marquee pdf/png for ingestion;
  upstream normalized entities downstream).
- `expected_output/` — the entities + `_expected_output.json` it **would** produce. Forecasting,
  Replenishment, and Planner also persist their concrete handoff artifacts:
  `forecast_result.json`, `replenishment_plan.json`, and `planner_decision.json`.

```bash
# start at Replenishment & Allocation in the supplier-delay path
cat 00_raw/IPF-003_supplier_delay_stockout_expedite/05_replenishment_allocation/agent_input.json
ls  00_raw/IPF-003_supplier_delay_stockout_expedite/05_replenishment_allocation/input/
cat 00_raw/IPF-003_supplier_delay_stockout_expedite/05_replenishment_allocation/expected_output/_expected_output.json
```

## Notes vs the loan / HLS reference

- Like loan (`00_raw/<bucket>/APP-XXX/`) and HLS (`00_raw/RKM-XXX_<path>/`), scenarios use a
  trackable prefix and the path in the folder name (`00_raw/IPF-XXX_<path>/`). Inventory adds a
  per-stage sub-structure because its workflow is a 5-agent chain, not a single decision —
  the same extension HLS makes for its 4-agent chain.
- `loan-mortgage-agents` collapses its post-extraction agents into one underwriting decision;
  inventory keeps the same single collapsed ground truth (`07`) but expresses it as an **e2e
  rollup** whose `stages[]` carry each agent's `agent_input` / `decision` / `expected_output`,
  so the chain is auditable per agent without adding standalone per-agent ground-truth files.
- **The budget cap (BG-300, 3× avg weekly demand) does not bind on any seed case** — the
  largest order (IPF-002, 584 units) sits well under its 1095-unit cap. The Planner Copilot
  budget-enforcement step still *runs* and is routed to human review in IPF-002 (`binding_constraint:
  none`, approved within cap); this is a deliberate, documented property of the M5-anchored
  data, not a missing case.
- Deliberately **not** modeled as a normalized ERP layer: a separate PO/TO table/entity and
  multi-warehouse allocation splits. The stage-level `replenishment_plan.json` is the explicit
  handoff artifact for the proposed order/expedite decision.
