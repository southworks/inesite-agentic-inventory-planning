# Test Cases — Decision Ground Truth

How to read the retail inventory-planning evaluation cases and trace each expected
outcome back to concrete Raw Layer files.

## Where the cases live

The 10 evaluation cases are JSON files in:

```text
07_decision_ground_truth/
```

Each case includes `scenario_id`, `scenario_type`, the `sku_id` / `store_ids` it concerns,
the `affected_weeks`, the `primary_reason`, `top_policy_refs` (into `06_policy_rag/`), and a
`summary_explanation`.

## Raw layer is organized by scenario

Each case has a **self-contained Raw Layer folder** so you can start a test flow by pointing
an agent at one directory:

```text
00_raw/<SCENARIO-ID>/<source_type>/...
```

These folders are **slices** of the canonical system exports in `00_raw/_full_exports/`
(filtered to the case's SKU/store/window), plus the marquee per-entity documents (shipment
receiving report + packing slip, supplier profile, promo brief). The canonical exports under
`_full_exports/` remain the single source of truth and are what the pipeline
(`generate_normalized_layers.py`) actually reads. See [RAW_LAYER.md](RAW_LAYER.md).

## Case index

| Case | Type | SKU @ store | Signal carried in the scenario folder | Real / injected |
| --- | --- | --- | --- | --- |
| `SEASONAL-01` | seasonal_trend | `FOODS_3_586` @ CA_1, TX_2 | POS (dual holiday peaks) + inventory + minor promo overlay | Real |
| `SEASONAL-02` | seasonal_trend | `HOUSEHOLD_1_334` @ TX_2 | POS (single steep Christmas ramp) + inventory | Real |
| `PROMO-01` | promotion_spike | `FOODS_3_252` @ TX_2 | POS (`PROMO_IND=1`, ×1.6 uplift) + `promo_calendar` + inventory | Injected |
| `PROMO-02` | promotion_spike | `HOBBIES_1_268` @ CA_1 | POS (×1.7 uplift) + `promo_calendar` + inventory | Injected |
| `SUPPLIER-DELAY-01` | supplier_disruption | `HOUSEHOLD_1_447` @ TX_2 | `supplier_shipments` SHP-0003 (14-day delay) + supplier + POS + inventory | Injected (supplier feed) |
| `SUPPLIER-DELAY-02` | supplier_disruption | `HOBBIES_1_048` @ CA_1 | `supplier_shipments` SHP-0005 (57.9% fill) + supplier + POS + inventory | Injected (supplier feed) |
| `STOCKOUT-01` | stockout_risk | `HOUSEHOLD_1_447` @ TX_2 | inventory (`BELOW_SAFETY_STOCK`, on_hand→0) + driving SHP-0003 + POS | Injected (inventory feed) |
| `STOCKOUT-02` | stockout_risk | `HOBBIES_1_048` @ CA_1 | inventory (on_hand 28 < safety 103) + driving SHP-0005 + POS | Injected (inventory feed) |
| `ANOMALY-01` | demand_anomaly | `HOBBIES_1_268` @ CA_1 | POS dip (`1,1,3`) on 2015-12-09…11, no promo/event | Injected |
| `ANOMALY-02` | demand_anomaly | `FOODS_3_252` @ CA_1 | POS spike (`36,73`) on 2015-11-04…05, `PROMO_IND=0` | Injected |

## How to trace a case to Raw files

```text
07_decision_ground_truth/<SCENARIO-ID>.json
  -> sku_id + store_ids + affected_weeks
  -> 00_raw/<SCENARIO-ID>/<source_type>/...        (the sliced, self-contained inputs)
  -> 00_raw/_full_exports/<source_type>/...        (the canonical, un-sliced originals)
```

Example — `STOCKOUT-01`:

```text
07_decision_ground_truth/STOCKOUT-01.json   (sku HOUSEHOLD_1_447 @ TX_2, weeks 12-07 & 12-14)
  -> 00_raw/STOCKOUT-01/inventory_snapshots/inventory_snapshot.csv   (on_hand 86 → 0, BELOW_SAFETY_STOCK)
  -> 00_raw/STOCKOUT-01/supplier_data/supplier_shipments.txt         (SHP-0003, 14-day delay)
  -> 00_raw/STOCKOUT-01/pos_transactions/pos_export_*.csv            (TX_2 demand context)
  -> canonical originals under 00_raw/_full_exports/...
```

## Quick lookup commands

List all cases:

```bash
ls dataset-seed/07_decision_ground_truth/*.json
```

Show one case:

```bash
jq '{scenario_id, scenario_type, sku_id, store_ids, affected_weeks, primary_reason, summary_explanation}' \
  dataset-seed/07_decision_ground_truth/STOCKOUT-01.json
```

Show the self-contained Raw inputs for one case:

```bash
find dataset-seed/00_raw/STOCKOUT-01 -type f
```
