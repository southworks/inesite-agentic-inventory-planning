# 05 Demand Signals Schema

Feature-engineered weekly time series per SKU — the output of the Feature & Causality
Agent, derived from `01_pos_transactions/` (the units) and `_source/m5_extract.json`
(the calendar). One file per SKU, broken out per store it sells at.

## Sample of required fields

```json
{
  "document_id": "DMD-HOBBIES_1_268",
  "document_type": "demand_signal",
  "document_date": "2016-01-03",
  "source_system": "signal_ingestion_agent",
  "sku_id": "HOBBIES_1_268",
  "category": "HOBBIES",
  "product_desc": "Puzzle 1000-Piece",
  "stores": {
    "CA_1": {
      "week_start_dates": ["2015-10-19", "..."],
      "weekly_units": [91, "..."],
      "rolling_3wk_avg": [91.0, "..."],
      "pct_change_vs_prior_week": [null, "..."],
      "avg_weekly_demand": 99,
      "statistical_anomaly_weeks": ["2015-11-02"],
      "promo_weeks": ["2015-11-02"],
      "holiday_weeks": ["2015-10-26", "..."],
      "scenario_refs": ["ANOMALY-01", "PROMO-02"]
    }
  }
}
```

## Required fields

- `sku_id`, `category`, `product_desc`
- `stores.<store_id>.week_start_dates`, `.weekly_units` (both length 11)
- `.rolling_3wk_avg`, `.pct_change_vs_prior_week` — trailing-window features
- `.avg_weekly_demand` — same number as `safety_stock_units` in `04_inventory/` for this SKU/store (1.0 week of cover, by construction)
- `.statistical_anomaly_weeks` — weeks containing a day where `|units - mean| > 2.5 * stdev` over the full window (computed, not asserted)
- `.promo_weeks`, `.holiday_weeks` — weeks overlapping a `03_promotions/` window or a National/Cultural/Religious calendar event
- `.scenario_refs` — cross-reference to `RAW_LAYER.md` / `07_decision_ground_truth/` scenario IDs that touch this SKU/store

## Notes

- One file per SKU — 6 files total. A SKU sold at both stores (e.g. `FOODS_3_586`) has both under `stores`; the two HOBBIES SKUs only have `CA_1` (real M5 data — they barely sell at `TX_2`, see `RAW_LAYER.md`).
- `statistical_anomaly_weeks` is a genuine computed feature (z-score on daily units), not a lookup of the known `ANOMALY-01`/`ANOMALY-02` scenarios — `scenario_refs` is the lookup. The two should usually agree; if they don't, that itself is interesting (a real anomaly the constants don't capture, or a flagged week with no actual outlier).
