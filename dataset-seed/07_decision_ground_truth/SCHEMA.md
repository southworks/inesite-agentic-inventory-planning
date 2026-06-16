# 07 Decision Ground Truth Schema

Expected forecast/replenishment outcome per scenario, for evaluating the Forecasting and
Replenishment & Allocation agents. One file per scenario (see `RAW_LAYER.md` for the full
scenario log), plus a `ground_truth.csv` rollup — same shape as the FSI dataset-seed's
`09_decision_ground_truth/`, adapted from a single decision per case to a forecast +
replenishment outcome per scenario.

## Sample of required fields

```json
{
  "document_id": "STOCKOUT-02",
  "document_type": "decision_ground_truth",
  "document_date": "2015-12-14",
  "source_system": "replenishment_ground_truth",
  "scenario_id": "STOCKOUT-02",
  "scenario_type": "stockout_risk",
  "sku_id": "HOBBIES_1_048",
  "store_ids": ["CA_1"],
  "affected_weeks": ["2015-12-14"],
  "avg_weekly_demand": 103,
  "target_on_hand_units": 154,
  "expected_forecast_units_per_week": { "CA_1|2015-12-14": 103 },
  "shortfall_units": 25,
  "recommended_replenishment_order_qty": 80,
  "expedite_required": false,
  "anomaly_flag": false,
  "primary_reason": "stockout_risk_after_fill_rate_shortfall",
  "top_policy_refs": ["SL-100", "RP-200", "RP-210"],
  "summary_explanation": "On-hand falls to 28, below the 103-unit safety stock — caused by SUPPLIER-DELAY-02's partial shipment."
}
```

## Required fields

- `scenario_id`, `scenario_type` (one of `seasonal_trend`, `promotion_demand_spike`, `supplier_delay`, `stockout_risk`, `demand_anomaly`)
- `sku_id`, `store_ids`, `affected_weeks`
- `expected_forecast_units_per_week`: `"{store}|{week_start}": units` — the forecast baseline for that scenario type (see Notes)
- `shortfall_units`, `recommended_replenishment_order_qty` (RP-200/RP-210), `expedite_required` (SP-410)
- `anomaly_flag`, `primary_reason`, `top_policy_refs`, `summary_explanation`

## How each field is computed

| `scenario_type` | `expected_forecast_units_per_week` | `recommended_replenishment_order_qty` |
|---|---|---|
| `seasonal_trend` | the real historical units for the holiday week (SN-500) | the holiday week's own demand (replace what's expected to sell) |
| `promotion_demand_spike` | `avg_weekly_demand * (1 + expected_uplift_pct/100)` (SN-510), capped by BG-300 | same as forecast, capped at `3x avg_weekly_demand` |
| `supplier_delay` | the undisrupted baseline (`avg_weekly_demand`) | `max(moq, shortfall_units)` where `shortfall_units = ordered_qty - received_qty` |
| `stockout_risk` | the undisrupted baseline (`avg_weekly_demand`) | `max(moq, shortfall_units)` where `shortfall_units = max(0, target_on_hand - on_hand - in_transit)`; `0` if already covered by an in-transit shipment (then `expedite_required=true` instead) |
| `demand_anomaly` | the undisrupted baseline (`avg_weekly_demand`) — compare against the actual depressed/inflated `01_pos_transactions/` units to see the deviation | `0` — demand-side noise, not a supply-side action |

## Notes

- 10 files, one per scenario in `RAW_LAYER.md`'s scenario log — 2 of each `scenario_type`.
- `ground_truth.csv` rolls up `scenario_id, sku_id, store_ids, scenario_type, recommended_replenishment_order_qty, anomaly_flag, expedite_required, primary_reason` for quick scoring.
- Every number here is calculable from `00_raw/` + the `06_policy_rag/` ref cited in `top_policy_refs` — `generate_normalized_layers.py` is the reference implementation of that calculation, not a hand-asserted answer key.
