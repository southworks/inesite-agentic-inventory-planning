# 04 Inventory Schema

Weekly inventory snapshot extracted from `00_raw/_full_exports/inventory_snapshots/inventory_snapshot.csv`,
enriched with a policy-computed reorder point (RP-200).

## Sample of required fields

```json
{
  "document_id": "INV-HOBBIES_1_048-CA_1-2015-12-14",
  "document_type": "inventory_snapshot",
  "document_date": "2015-12-14",
  "source_system": "inventory_management_system",
  "sku_id": "HOBBIES_1_048",
  "store_id": "CA_1",
  "on_hand_units": 28,
  "in_transit_units": 101,
  "safety_stock_units": 103,
  "target_on_hand_units": 154,
  "reorder_point_units": 412,
  "lead_time_cover_gap_units": 258,
  "status": "BELOW_SAFETY_STOCK"
}
```

## Required fields

- `sku_id`, `store_id`, `on_hand_units`, `in_transit_units`, `safety_stock_units`
- `target_on_hand_units`: the SKU/store's healthy on-hand level (SL-100, ~1.5 weeks of cover)
- `reorder_point_units`: RP-200's `safety_stock_units + round(avg_weekly_demand * lead_time_days / 7)`
- `lead_time_cover_gap_units`: `max(0, reorder_point_units - target_on_hand_units)`
- `status`: `OK` or `BELOW_SAFETY_STOCK` — **this is the dataset's actual reorder trigger**, not `reorder_point_units` (see Notes)

## Notes

- One file per (SKU, store, snapshot date) — 110 files total, weekly cadence.
- `lead_time_cover_gap_units` is **not** a bug when it's non-zero: this dataset's target-on-hand policy is a flat 1.5 weeks of cover for every SKU regardless of its supplier's lead time, so `reorder_point_units` (which does scale with lead time) lands above `target_on_hand_units` for longer-lead SKUs. The gap is small for the 5-6 day FOODS suppliers and largest for the 21-24 day HOBBIES import suppliers — which is the structural reason a single disrupted HOBBIES shipment becomes a stockout (`STOCKOUT-02`) while the same disruption on a FOODS SKU would not. See `replenishment_policy.txt` (RP-200) and `RAW_LAYER.md`.
