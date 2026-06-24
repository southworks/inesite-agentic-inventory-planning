# Common JSON Fields Schema

Common fields for all normalized JSON documents in the dataset (`01_pos_transactions/`
through `07_decision_ground_truth/`). Mirrors the FSI dataset-seed's common fields,
adapted to a signal-based domain that has no single "case" identifier — the closest
domain key here is `sku_id` (`+ store_id` where relevant), not a borrower/application ID.

## Sample of required fields

```json
{
  "document_id": "POS-FOODS_3_586-CA_1-2015-10-19",
  "document_type": "pos_transaction_batch",
  "document_date": "2015-10-19",
  "source_system": "pos_export"
}
```

## Field definitions

- `document_id`: unique identifier for the document, scoped to its folder's entity (SKU+store+week for POS/inventory, supplier_id for supplier data, event_id for promotions, SKU for demand signals, scenario_id for ground truth).
- `document_type`: one of `pos_transaction_batch`, `supplier_profile`, `promotion_event`, `inventory_snapshot`, `demand_signal`, `decision_ground_truth` — consistent with the folder where the file is stored.
- `document_date`: anchor date in `YYYY-MM-DD` format (batch/snapshot date, or the most relevant date for the record).
- `source_system`: the system that produced the underlying signal (`pos_export`, `vendorhub_erp`, `pricing_calendar_system`, `inventory_management_system` for the Signal-Ingestion-normalized layers 01-04; `feature_causality_agent` for derived feature data in 05; `inventory_planning_ground_truth` for the 07/ e2e rollups).

## Notes

- `document_id` must be unique within its folder.
- Every record carries `sku_id` (and `store_id` where the record is store-scoped) so cross-layer joins are always possible without a separate case key.
- `document_date` orders evidence for retrieval and auditability, same as in the FSI dataset.
