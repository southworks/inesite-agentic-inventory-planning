# Test Cases — End-to-end workflow scenarios

How to read the retail inventory-planning evaluation cases and trace each expected outcome
through the full agent chain back to concrete Raw Layer files.

The test cases are **end-to-end**: each scenario is one full pass through the workflow
(Orchestrator → Signal Ingestion → Feature & Causality → Forecasting → Replenishment &
Allocation → Planner Copilot), differing at the human-in-the-loop gates and at the
supply/demand signal that drives the outcome. The set is defined once in
[`scenarios.py`](scenarios.py). See [HANDOFF.md](HANDOFF.md) for the per-agent handoff map and
[TESTING_GUIDE.md](TESTING_GUIDE.md) for the high-level demo runbook (how to drive each scenario by
injecting the prepared per-agent folders — no terminal required).

## Where the cases live

```text
07_decision_ground_truth/IPF-XXX.json     ← one e2e rollup per scenario
00_raw/IPF-XXX_<path>/                     ← the self-contained per-agent folders
```

Each rollup includes `scenario_id`, `path`, `scenario_type`, the `sku_id` / `store_ids` /
`affected_weeks`, the `orchestrator_request`, the ordered `stages[]` (each with the agent's
`agent_input`, `decision`, `gate`, and `expected_output`), the `final_outcome`,
`required_human_review`, `primary_reason`, `top_policy_refs`, and a `summary_explanation`.

## Case index

| Case | Path | SKU @ store | Drives the outcome | Final outcome | HITL gate |
| --- | --- | --- | --- | --- | --- |
| `IPF-001` | `seasonal_happy_path` | `HOUSEHOLD_1_334` @ TX_2 | Real, steep Christmas-week ramp (peak 208) | `order_approved` (208) | none |
| `IPF-002` | `promotion_spike_budget_review` | `FOODS_3_252` @ TX_2 | 20% promo, declared uplift 60% (SN-510) | `order_approved_within_budget` (584) | Planner budget review |
| `IPF-003` | `supplier_delay_stockout_expedite` | `HOUSEHOLD_1_447` @ TX_2 | SHP-0003 14 days late; on-hand 86→0 below safety | `expedite_required` (qty 0, in-transit covers) | Planner service-level |
| `IPF-004` | `partial_fill_stockout_reorder` | `HOBBIES_1_048` @ CA_1 | SHP-0005 57.9% filled; on-hand 28 < safety 103 | `reorder_approved` (80 = MOQ) | none |
| `IPF-005` | `demand_anomaly_no_action` | `HOBBIES_1_268` @ CA_1 | Unexplained dip (1,1,3), no promo/event/disruption | `flagged_anomaly_no_action` (qty 0) | Forecasting anomaly review |

The five scenario *types* of the underlying signal (seasonal trend, promotion spike, supplier
disruption, stockout risk, demand anomaly) are all preserved — `IPF-003`/`IPF-004` fold the
supplier-disruption signal and the stockout it causes into one e2e flow, the way the real
chain experiences them.

## How to trace a case through the chain

```text
07_decision_ground_truth/IPF-003.json
  -> orchestrator_request + stages[] (per-agent agent_input / decision / expected_output)
  -> 00_raw/IPF-003_supplier_delay_stockout_expedite/
       01_orchestrator/request.json
       02_signal_ingestion/      input/ (raw POS/inventory/supplier csv+txt + SHP-0003 pdf/png)   expected_output/ (01,02,04 entities)
       03_feature_causality/     input/ (01 POS entities)                                          expected_output/ (05 DMD)
       04_forecasting/           input/ (05 DMD)                                                   expected_output/ (forecast_result.json)
       05_replenishment_allocation/ input/ (forecast_result + 04 INV affected weeks + 02 SUP-003)  expected_output/ (replenishment_plan.json)
       06_planner_copilot/       input/ (replenishment_plan + 06 policy: SL/BG/RP)                 expected_output/ (planner_decision.json)
  -> 00_raw/_full_exports/...   (the canonical, un-sliced originals)
```

## How each `expected_output` is computed

Every number is calculable from `00_raw/` + the `06_policy_rag/` ref cited in
`top_policy_refs` — `generate_normalized_layers.py` is the reference implementation, not a
hand-asserted answer key.

| `scenario_type` | forecast (Forecasting) | proposed order (Replenishment) | budget enforcement (Planner) |
|---|---|---|---|
| `seasonal_trend` | the holiday week's own historical units (SN-500) | the holiday week's demand | `min(proposed, 3× avg)` (BG-300) |
| `promotion_demand_spike` | `avg × (1 + expected_uplift_pct/100)` (SN-510) | same as forecast | budget gate reviewed; within cap |
| `stockout_risk` (delay) | undisrupted baseline (`avg`) | `0` if in-transit covers the gap → `expedite_required` (SP-410/SL-100) | service-level gate |
| `stockout_risk` (partial fill) | undisrupted baseline (`avg`) | `max(MOQ, shortfall)` (RP-200/RP-210) | within budget |
| `demand_anomaly` | undisrupted baseline (`avg`) — compare to the depressed/inflated actuals | `0` — demand-side noise, no supply action | n/a |

## Quick lookup commands

```bash
ls dataset-seed/07_decision_ground_truth/IPF-*.json                 # all cases
jq '{scenario_id, path, final_outcome, required_human_review, summary_explanation}' \
  dataset-seed/07_decision_ground_truth/IPF-003.json               # one case (rollup)
jq '.stages[] | {order, stage, decision, gate, expected_output}' \
  dataset-seed/07_decision_ground_truth/IPF-003.json               # its per-agent handoff
find dataset-seed/00_raw/IPF-003_supplier_delay_stockout_expedite -type f   # its self-contained inputs
cat dataset-seed/07_decision_ground_truth/ground_truth.csv         # 5-row rollup for scoring
```
