# 03 Promotions Schema

Promotional/price calendar event extracted from `00_raw/promotions/promo_calendar.csv`,
enriched with the uplift actually observed in `01_pos_transactions/` for that SKU/store/window.

## Sample of required fields

```json
{
  "document_id": "PROMO-2015-11-A",
  "document_type": "promotion_event",
  "document_date": "2015-11-02",
  "source_system": "pricing_calendar_system",
  "event_id": "PROMO-2015-11-A",
  "sku_id": "FOODS_3_252",
  "store_id": "TX_2",
  "start_date": "2015-11-02",
  "end_date": "2015-11-08",
  "discount_pct": 20,
  "expected_uplift_pct": 60,
  "observed_units_in_window": 527,
  "baseline_units_in_window": 330.0,
  "observed_uplift_pct": 59.7
}
```

## Required fields

- `event_id`, `sku_id`, `store_id`, `start_date`, `end_date`, `discount_pct`, `expected_uplift_pct` (declared, from the raw calendar)
- `observed_units_in_window`, `baseline_units_in_window`, `observed_uplift_pct` (computed — see Notes)

## Notes

- One file per promo calendar entry — 4 files total (2 with a ground-truth scenario attached: `PROMO-01`/`PROMO-02`; 2 minor/non-scenario promos for calendar realism — see `RAW_LAYER.md`).
- `baseline_units_in_window` is the average daily units over the **14 days immediately preceding** the promo window (not "every other day in the dataset" — that would pull in the Thanksgiving/Christmas peak weeks and understate the true lift for a promo run in a quiet week).
- `observed_uplift_pct` should land close to `expected_uplift_pct` for the two declared scenarios — a large divergence between them would itself be a signal worth flagging.
