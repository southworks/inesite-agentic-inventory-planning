# Raw Layer — Retail Inventory Planning & Trend Forecasting Dataset Seed

Tracks the structure, source data, and scenario coverage of the Raw layer (`00_raw/`).
Update this file whenever a new scenario, source type, or SKU/store is added.

Reference user story: [US 128593](https://dev.azure.com/southworks/inesite/_workitems/edit/128593).

## What is the Raw layer, and why it comes first here

Unlike the FSI scenario (where Raw `.txt` documents are *rendered from* the Bronze JSON
ground truth), this scenario is signal-based, not document-based. The Raw layer is the
**input** the five-agent pipeline ingests — there is no upstream ground truth to render it
from. So the order is reversed:

```
Raw (.csv/.txt system exports)  →  Signal Ingestion Agent  →  Normalized JSON (01-05/)  →  Forecasting / Replenishment agents  →  Ground truth (07/)
```

`01_pos_transactions/` … `05_demand_signals/` (added in a follow-up step) represent what the
**Signal Ingestion Agent extracts and validates** from these raw exports.

## Source data

Real, coherent retail signals are pulled from **M5 Forecasting - Accuracy** (Walmart),
via the no-login GitHub mirror published by Nixtla:

```bash
curl -sL "https://github.com/Nixtla/m5-forecasts/raw/main/datasets/m5.zip" -o m5.zip
unzip m5.zip -d unzipped/
# uses: calendar.csv, sales_train_evaluation.csv, sell_prices.csv
```

From the full M5 dataset (3,049 items × 10 stores × ~5.4 years) we extracted a small,
curated slice — **6 real SKUs, 2 real stores, 77 real days (11 weeks)** of actual daily
unit sales and prices — into [`_source/m5_extract.json`](_source/m5_extract.json) (~28 KB,
committed). The full M5 CSVs (~360 MB combined) are **not** committed; only this small
extract is, so the Raw layer is reproducible without re-downloading anything.

| | Selection |
|---|---|
| Window | `2015-10-19` (Mon) → `2016-01-03` (Sun) — 77 days / 11 weeks, spans Halloween, Veterans Day, Thanksgiving, Chanukah, Christmas, New Year |
| Stores | `CA_1` (California), `TX_2` (Texas) |
| Categories | `FOODS`, `HOUSEHOLD`, `HOBBIES` (3 of M5's 3 top-level categories) |
| SKUs | `FOODS_3_586`, `FOODS_3_252`, `HOUSEHOLD_1_447`, `HOUSEHOLD_1_334` (both stores) and `HOBBIES_1_048`, `HOBBIES_1_268` (`CA_1` only — these items are real but low-volume/sparse at `TX_2`, so we only carry them at the store where they actually sell) |

M5 item codes are anonymized; we assign illustrative, consistent product names to them
(see `PRODUCT_NAMES` in `generate_raw_layer.py`) for realism — e.g. `FOODS_3_586` →
"Frozen Lasagna Family Pack". The units/prices themselves are real M5 values.

**Supplier, promotion, and inventory data have no public real-world equivalent** — M5 only
covers POS + price + calendar. Those three source types are synthesized to be internally
coherent with the real POS baseline and with each other (see scenario log below).

## Folder structure — organized by scenario / test case

The Raw layer is organized **by scenario / test case, not by format**. Because this workflow
is *signal-based* (the same system export carries signals for several scenarios — one
`inventory_snapshot.csv` drives both STOCKOUT-01 and STOCKOUT-02; the POS batches are weekly
and span every SKU), a strict per-scenario partition requires **duplication**. So the layout
keeps one canonical copy and adds self-contained per-scenario slices:

```
dataset-seed/
├── _source/
│   └── m5_extract.json              ← curated real M5 extract (committed, ~28 KB)
├── 00_raw/
│   ├── _full_exports/               ← CANONICAL, un-sliced system exports (single source of truth)
│   │   ├── pos_transactions/pos_export_<start>_to_<end>.csv   ← 11 weekly POS batches
│   │   ├── supplier_data/supplier_master.txt, supplier_shipments.txt
│   │   ├── promotions/promo_calendar.csv
│   │   ├── inventory_snapshots/inventory_snapshot.csv
│   │   └── <source_type>/*.pdf, *.png   ← renderings, co-located by source type
│   ├── SEASONAL-01/ SEASONAL-02/    ← per-scenario SLICES (sku/store/window-filtered)
│   ├── PROMO-01/ PROMO-02/          ←   each holds <source_type>/ csv|txt slices +
│   ├── SUPPLIER-DELAY-01/ -02/      ←   the marquee pdf/png documents for that case
│   ├── STOCKOUT-01/ -02/
│   └── ANOMALY-01/ -02/
├── generate_raw_layer.py            ← writes _full_exports/ from _source/, then the slices
└── generate_agent_documents.py      ← writes pdf/png renderings + per-scenario copies
```

**Canonical:** 15 csv/txt (11 POS batches + 2 supplier + 1 promo + 1 inventory) + 66 PDF/PNG
renderings = 81 files under `_full_exports/`. **Per-scenario:** ~145 sliced/copied files
across the 10 scenario folders (deliberate duplicates of the canonical data, scoped to one
scenario each). See [AGENT_INPUTS.md](AGENT_INPUTS.md) and [TEST_CASES.md](TEST_CASES.md).

**Single source of truth:** only `_full_exports/` feeds the pipeline —
`generate_normalized_layers.py` reads exclusively from there, so the scenario slices never
affect the normalized layers or ground truth (verified: regenerating leaves `01_*`–`07_*`
byte-identical). The `SCENARIOS` registry in `generate_raw_layer.py` defines, per scenario,
which source types + SKU(s)/store(s)/entities each slice carries.

## Generation script

```bash
cd dataset-seed
rm -rf 00_raw                          # clear the old tree (layout changed)
python3 generate_raw_layer.py          # _full_exports/ csv|txt + per-scenario slices
pip install -r requirements.txt
python3 generate_agent_documents.py    # pdf/png renderings + per-scenario copies
python3 generate_normalized_layers.py  # 01_*–07_* from _full_exports/ only
```

`generate_raw_layer.py` reads `_source/m5_extract.json` plus the scenario constants
(`PROMO_EVENTS`, `ANOMALIES`, `SUPPLIER_SHIPMENTS`, `STOCKOUT_WINDOWS`, and the `SCENARIOS`
registry), writes the canonical exports to `00_raw/_full_exports/`, then writes the
sliced per-scenario folders. Stdlib only.

**When to re-run:** after editing any scenario constant, the `SCENARIOS` registry, adding a
SKU/store, or extending a formatter.

## Document/file types

| File(s) | Format | Simulates |
|---|---|---|
| `pos_transactions/pos_export_*.csv` | CSV, `TRANS_DATE,STORE_ID,SKU,PRODUCT_DESC,UNITS_SOLD,UNIT_PRICE,NET_SALES,PROMO_IND` | Weekly batch export from the store POS system |
| `supplier_data/supplier_master.txt` | Fixed-block text | ERP procurement module vendor master extract |
| `supplier_data/supplier_shipments.txt` | Fixed-block text | ERP receiving module shipment receipt log |
| `promotions/promo_calendar.csv` | CSV, `EVENT_ID,SKU,STORE_ID,START_DATE,END_DATE,DISCOUNT_PCT,EXPECTED_UPLIFT_PCT` | Pricing/promotions system calendar export |
| `inventory_snapshots/inventory_snapshot.csv` | CSV, `SNAPSHOT_DATE,STORE_ID,SKU,ON_HAND_UNITS,IN_TRANSIT_UNITS,SAFETY_STOCK_UNITS,STATUS` | Weekly warehouse/store inventory management system snapshot |

`06_credit/`-equivalent note: there is no analogous "pulled by the system, not submitted by
a customer" exclusion here — all four source types are genuine system-of-record exports
ingested by the Signal Ingestion Agent.

---

## Scenario log

10 scenarios across the 6 SKUs — 2 of each required type. "Real" means the signal exists
in the M5 data unmodified; "Injected" means `generate_raw_layer.py` deliberately adjusts
the real baseline (documented exactly, like the FSI deny/manual-review inconsistencies).

### Seasonal trend (real, no injection)

#### SEASONAL-01 — `FOODS_3_586` @ `CA_1` and `TX_2`

Real dual holiday peaks: Thanksgiving week (`2015-11-23`–`29`) and the pre-Christmas week
(`2015-12-14`–`20`), with a post-New-Year lull (`2015-12-28`–`2016-01-03`).

- `TX_2` weekly units: `586, 688, 631, 641, 632, 734, 684, 717, 782, 743, 551` — peaks at Thanksgiving (734) and pre-Christmas (782), trough at New Year (551).
- `CA_1` shows the same two-peak shape at smaller volume. **Note:** `CA_1`'s Thanksgiving week also carries a minor promo overlay (see `PROMO-2015-11-D` below, +10%) layered on top of the organic lift — the underlying seasonal shape is still real.

#### SEASONAL-02 — `HOUSEHOLD_1_334` @ `TX_2`

Real, sharp ramp into Christmas week — a gift-wrap-adjacent item with a different seasonal
signature than SEASONAL-01 (single steep ramp vs. dual holiday peaks): weekly units
`79, 101, 111, 87, 81, 58, 58, 134, 128, 208, 175` — more than triples from its `58`-unit
trough (week of `2015-11-30`) to `208` in Christmas week (`2015-12-21`–`27`). `CA_1` does
not show the same ramp (regional variation — left unmodified, real).

### Promotion demand spike (deliberate injection)

#### PROMO-01 — `FOODS_3_252` @ `TX_2`, `2015-11-02`–`08`

`PROMO-2015-11-A`: 20% discount (`$1.58` → `$1.26`), units uplifted ×1.6 over the real
baseline. `promo_calendar.csv` declares `expected_uplift_pct=60`. Visible in
`pos_export_2015-11-02_to_2015-11-08.csv` as `PROMO_IND=1` for every `TX_2` row that week,
with weekly total well above the SKU's typical `TX_2` baseline (~52 units/day).

#### PROMO-02 — `HOBBIES_1_268` @ `CA_1`, `2015-11-02`–`08`

`PROMO-2015-11-B`: 25% discount (`$0.48` → `$0.36`), units uplifted ×1.7, `expected_uplift_pct=70`.
Same week as PROMO-01 but a different SKU/store — demonstrates two unrelated, simultaneous
promotions a Replenishment agent must track independently.

Two additional minor, non-scenario promos are also in `promo_calendar.csv` for calendar
realism (no ground-truth case attached): `PROMO-2015-12-C` (`HOUSEHOLD_1_447`@`CA_1`, 10% off)
and `PROMO-2015-11-D` (`FOODS_3_586`@`CA_1`, 15% off, overlaid on the real Thanksgiving peak).

### Supplier delay / disruption (synthetic, supplier feed only — no POS adjustment)

#### SUPPLIER-DELAY-01 — `SUP-003` → `HOUSEHOLD_1_447`

`SHP-0003`: ordered `2015-11-25`, expected `2015-12-05`, **actual `2015-12-19`** — 14-day
delay ("carrier capacity constraint during holiday peak season"). Drives STOCKOUT-01.

#### SUPPLIER-DELAY-02 — `SUP-005` → `HOBBIES_1_048`

`SHP-0005`: delivered on time (`2015-12-15`) but only **57.9% filled** (139 of 240 units
ordered) — "vendor backorder on imported components". Drives STOCKOUT-02.

### Stockout risk (consequence of the supplier disruptions, asserted in `inventory_snapshot.csv`)

#### STOCKOUT-01 — `HOUSEHOLD_1_447` @ `TX_2`

`on_hand` falls from a healthy `227` to `86` (`2015-12-07`) and `0` (`2015-12-14`) —
both `BELOW_SAFETY_STOCK` (151) — while `in_transit=350` (the delayed shipment) sits stuck.
Recovers to `227` on `2015-12-21`, right as the delayed delivery lands, but **after** the
store has already gone into Christmas week stocked out.

#### STOCKOUT-02 — `HOBBIES_1_048` @ `CA_1`

`on_hand=28` on `2015-12-14` (below safety stock `103`), `in_transit=101` — the re-order for
the 101-unit shortfall left by the partial-fill shipment.

### Demand anomaly (deliberate injection, unexplained — no promo/event/disruption attached)

#### ANOMALY-01 — `HOBBIES_1_268` @ `CA_1`, dip on `2015-12-09`–`11`

Units drop to `1, 1, 3` against a week that otherwise reads `13, 42, …, 7, 13`
(`pos_export_2015-12-07_to_2015-12-13.csv`). No promo flag, no calendar event, no
supplier disruption for this SKU — the Forecasting agent has nothing to explain it with.

#### ANOMALY-02 — `FOODS_3_252` @ `CA_1`, spike on `2015-11-04`–`05`

Units jump to `36, 73` against neighboring days of `21, 21, …, 36, 51, 45`. Same week as
PROMO-01, but a **different store** with `PROMO_IND=0` — the spike cannot be explained by
the promo calendar, testing whether the agent correctly scopes promo attribution per store.

---

## Adding a new scenario

1. Pick a real `(item_id, store_id)` pair from M5 (or extend `_source/m5_extract.json` with
   a new extraction following the "Source data" recipe above) and add it to `PRODUCT_NAMES`.
2. Add the scenario to the relevant constant in `generate_raw_layer.py`
   (`PROMO_EVENTS`, `ANOMALIES`, `SUPPLIER_SHIPMENTS`, or `STOCKOUT_WINDOWS`).
3. Add an entry to the `SCENARIOS` registry in `generate_raw_layer.py` declaring the
   source types + SKU(s)/store(s)/entities its per-scenario slice should carry.
4. Re-run the full pipeline (see **Generation script**: `generate_raw_layer.py` →
   `generate_agent_documents.py` → `generate_normalized_layers.py`).
5. Add a new entry to the **Scenario log** above and a row to [TEST_CASES.md](TEST_CASES.md),
   following the existing format (what's real vs. injected, which file(s) carry the signal).
