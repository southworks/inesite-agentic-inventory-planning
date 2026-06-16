# 01 POS Transactions Schema

Weekly transaction batch extracted from a `00_raw/pos_transactions/pos_export_*.csv`
system export — what the Signal Ingestion Agent validated and normalized for one
SKU+store+week.

## Sample of required fields

```json
{
  "document_id": "POS-FOODS_3_586-CA_1-2015-10-19",
  "document_type": "pos_transaction_batch",
  "document_date": "2015-10-19",
  "source_system": "pos_export",
  "sku_id": "FOODS_3_586",
  "store_id": "CA_1",
  "category": "FOODS",
  "product_desc": "Frozen Lasagna Family Pack",
  "batch_week_start": "2015-10-19",
  "batch_week_end": "2015-10-25",
  "daily_records": [
    { "date": "2015-10-19", "units_sold": 35, "unit_price": 1.68, "revenue": 58.8, "promo_flag": false }
  ],
  "weekly_summary": {
    "total_units_sold": 264,
    "total_revenue": 443.52,
    "promo_days": 0,
    "avg_unit_price": 1.68
  }
}
```

## Required fields

- `sku_id`, `store_id`, `category`, `product_desc`
- `batch_week_start`, `batch_week_end`
- `daily_records[].date`, `.units_sold`, `.unit_price`, `.revenue`, `.promo_flag`
- `weekly_summary.total_units_sold`, `.total_revenue`, `.promo_days`, `.avg_unit_price`

## Notes

- One file per (SKU, store, week) — 110 files total (10 SKU/store pairs × 11 weeks), matching the raw layer's weekly batch granularity.
- `daily_records` always has exactly 7 entries.
- `promo_flag = true` days should match a record in `03_promotions/` for that SKU/store/date range — see `RAW_LAYER.md` scenario log for the two store/SKU pairs where a flagged spike has no matching promo (the demand anomalies).
