# 07 Decision Ground Truth Schema

End-to-end workflow rollups, one per scenario (`IPF-XXX.json`), plus a `ground_truth.csv`
rollup. Each scenario is a full pass through the five-agent chain (Signal Ingestion → Feature
& Causality → Forecasting → Replenishment & Allocation → Planner Copilot); the five scenarios
differ at the human-in-the-loop gates and at the supply/demand signal that drives the outcome.
Defined once in [`scenarios.py`](../scenarios.py); the `00_raw/IPF-XXX_<path>/` folders are
built from these rollups by [`build_scenario_folders.py`](../build_scenario_folders.py).

Same convention as the FSI (`09_decision_ground_truth/`) and HLS (`09_decision_ground_truth/`)
datasets — a single collapsed ground truth per case, expressed here as an e2e rollup whose
`stages[]` carry each agent's expected handoff.

## Sample of required fields

```json
{
  "document_id": "IPF-003",
  "document_type": "decision_ground_truth",
  "document_date": "2015-12-07",
  "source_system": "inventory_planning_ground_truth",
  "scenario_id": "IPF-003",
  "scenario_kind": "e2e_workflow_path",
  "scenario_type": "stockout_risk",
  "path": "supplier_delay_stockout_expedite",
  "title": "...",
  "scenario_folder": "IPF-003_supplier_delay_stockout_expedite",
  "sku_id": "HOUSEHOLD_1_447",
  "store_ids": ["TX_2"],
  "affected_weeks": ["2015-12-07", "2015-12-14"],
  "avg_weekly_demand": 151,
  "target_on_hand_units": 227,
  "orchestrator_request": { "request_id": "IPF-003-REQ", "intent": "...", "scope": {...}, "routed_to": [...] },
  "stages": [
    {
      "order": 4,
      "stage": "replenishment_allocation",
      "agent": "replenishment_allocation_agent",
      "agent_input": { "task": "recommend_targets_and_orders_and_create_pos_tos", "scope": {...} },
      "gate": null,
      "policy_refs": ["SL-100", "RP-200", "SP-410"],
      "decision": "expedite_flagged_no_new_order",
      "expected_output": { "proposed_order_qty": 0, "shortfall_units": 0, "target_on_hand_units": 227, "expedite_required": true },
      "raw_layer_folder": "00_raw/IPF-003_supplier_delay_stockout_expedite/02_forecasting/"
    }
  ],
  "final_outcome": "expedite_required",
  "required_human_review": true,
  "primary_reason": "stockout_risk_pending_delayed_shipment",
  "top_policy_refs": ["SL-100", "RP-200", "SP-410"],
  "summary_explanation": "..."
}
```

## Required fields

- `scenario_id`, `scenario_kind` (`e2e_workflow_path`), `scenario_type` (one of
  `seasonal_trend`, `promotion_demand_spike`, `stockout_risk`, `demand_anomaly`), `path`
- `sku_id`, `store_ids`, `affected_weeks`, `avg_weekly_demand`, `target_on_hand_units`
- `orchestrator_request` — the optional Planning request the orchestrator routes
- `stages[]` — the five agent stages in order; each carries:
  - `order`, `stage`, `agent`, `agent_input` (the payload that starts the agent in isolation)
  - `gate` (the HITL result: `quality_validated`, `anomaly_review`, `human_review`,
    `auto_approved`, or `null`), `policy_refs`
  - `decision` and `expected_output` — qualitative for ingestion/features, numeric for
    forecasting/replenishment/planner (see table below)
  - `raw_layer_folder` — present only on `signal_ingestion` and `forecasting` stages; points to
    the materialized `00_raw/` folder for that MCP stage. Other stages validate via `stages[]`
    and workflow memory.
- `final_outcome`, `required_human_review`, `primary_reason`, `top_policy_refs`,
  `summary_explanation`

## How each stage's `expected_output` is computed

| Stage | `expected_output` |
|---|---|
| `signal_ingestion` | `normalized_layers` produced, `quality_status` |
| `feature_causality` | `avg_weekly_demand`, `statistical_anomaly_weeks`, `promo_weeks`, `holiday_weeks` (+ `observed_uplift_pct` for promo paths) |
| `forecasting` | `expected_forecast_units_per_week` (SN-500 holiday / SN-510 promo / `avg` baseline), `anomaly_flag` |
| `replenishment_allocation` | `proposed_order_qty`, `shortfall_units`, `target_on_hand_units`, `expedite_required` (RP-200/RP-210, SP-410) |
| `planner_copilot` | `approved_order_qty` = `min(proposed, BG-300 cap)`, `budget_cap_units`, `binding_constraint`, `required_human_review`, `final_outcome` |

## Notes

- 5 files, one per scenario in `scenarios.py` — see [TEST_CASES.md](../TEST_CASES.md).
- `ground_truth.csv` rolls up `scenario_id, path, sku_id, store_ids, scenario_type,
  approved_order_qty, anomaly_flag, expedite_required, required_human_review, final_outcome`
  for quick scoring.
- Only Signal Ingestion and Forecasting have materialized folders under `00_raw/`:
  `01_signal_ingestion/expected_output/` (normalized entities) and
  `02_forecasting/expected_output/forecast_result.json`. Replenishment and Planner Copilot
  expected outputs live in `scenario.json` → `stages[]` only.
- Every number is calculable from `00_raw/` + the `06_policy_rag/` refs cited in
  `top_policy_refs` — `generate_normalized_layers.py` is the reference implementation of that
  calculation, not a hand-asserted answer key.
- The budget cap (BG-300, 3× avg weekly demand) does not bind on any seed case; the Planner
  Copilot budget step still runs and is routed to human review in `IPF-002` (approved within
  cap). See [HANDOFF.md](../HANDOFF.md).
