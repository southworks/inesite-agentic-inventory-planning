# 02 Supplier Data Schema

Supplier profile extracted from `00_raw/_full_exports/supplier_data/supplier_master.txt` and
`supplier_shipments.txt` — one file per supplier, with its shipment history nested.

## Sample of required fields

```json
{
  "document_id": "SUP-005",
  "document_type": "supplier_profile",
  "document_date": "2016-01-03",
  "source_system": "vendorhub_erp",
  "supplier_id": "SUP-005",
  "name": "Pinecrest Craft Imports",
  "sku_ids": ["HOBBIES_1_048"],
  "lead_time_days": 21,
  "moq": 80,
  "fill_rate_pct": 96.0,
  "reliability_score": 4.5,
  "performance_flag": "ok",
  "shipments": [
    {
      "shipment_id": "SHP-0005",
      "sku_id": "HOBBIES_1_048",
      "ordered_date": "2015-12-01",
      "expected_date": "2015-12-15",
      "actual_date": "2015-12-15",
      "ordered_qty": 240,
      "received_qty": 139,
      "fill_rate_pct": 57.9,
      "disrupted": true,
      "disruption_reason": "Vendor backorder on imported components — partial shipment (fill rate 57.9%)"
    }
  ]
}
```

## Required fields

- `supplier_id`, `name`, `sku_ids`, `lead_time_days` (nominal), `moq`, `fill_rate_pct` (nominal), `reliability_score`
- `performance_flag`: `"review_required"` if `reliability_score < 4.0`, else `"ok"` (SP-410)
- `shipments[]`: every shipment received in the window, with `disrupted`/`disruption_reason` set per SP-400/SP-410

## Notes

- One file per supplier — 6 files total, one SKU each in this dataset (a real supplier could carry more).
- `fill_rate_pct` at the top level is the supplier's nominal/master rate; the per-shipment `fill_rate_pct` inside `shipments[]` is what was actually observed on that delivery and is what SP-400 checks.
