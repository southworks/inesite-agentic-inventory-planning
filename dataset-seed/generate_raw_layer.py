#!/usr/bin/env python3
"""
Generate the Raw layer (dataset-seed/00_raw/) for the Retail inventory planning &
trend forecasting dataset-seed.

Source of truth: dataset-seed/_source/m5_extract.json — a small, curated extract
(10 real item/store daily-sales series + real prices + real calendar, 77 days)
pulled from the M5 Forecasting - Accuracy (Walmart) competition dataset. See
RAW_LAYER.md ("Source data") for exactly how that extract was produced.

This script:
  1. Loads the real M5 extract (units sold, prices, calendar).
  2. Applies deliberate, documented adjustments for the scenarios that are NOT
     naturally present in the real data (promotions, anomalies). Seasonal trend
     and the stockout-driving supplier disruptions need no POS adjustment — they
     are either real (seasonal) or expressed only in the supplier/inventory feeds.
  3. Writes realistic system-export files (POS weekly batches, ERP supplier
     feed, promo/price calendar, inventory snapshots) to 00_raw/.

Running it is idempotent — it overwrites 00_raw/ from the extract + scenario
constants below every time.
"""

import csv
import json
from pathlib import Path

BASE = Path(__file__).resolve().parent
RAW = BASE / "00_raw"
# Canonical, un-sliced system exports. This is the single source of truth that the
# Signal Ingestion / normalized-layer generator reads. The per-scenario, per-agent folders
# (00_raw/IPF-XXX_<path>/<stage>/) are demo-friendly slices, built by build_scenario_folders.py.
CANON = RAW / "_full_exports"
EXTRACT_PATH = BASE / "_source" / "m5_extract.json"

# ─── Reference data ──────────────────────────────────────────────────────────

PRODUCT_NAMES = {
    "FOODS_3_586": "Frozen Lasagna Family Pack",
    "FOODS_3_252": "Sparkling Water 12-Pack",
    "HOUSEHOLD_1_447": "Paper Towels 6-Roll",
    "HOUSEHOLD_1_334": "Gift Wrap Assortment",
    "HOBBIES_1_048": "Craft Paint Set",
    "HOBBIES_1_268": "Puzzle 1000-Piece",
}

STORE_NAMES = {
    "CA_1": "Store CA-1 (California)",
    "TX_2": "Store TX-2 (Texas)",
}

# One supplier per SKU. lead_time/moq/fill_rate are NOMINAL values — the two
# disrupted shipments below override fill_rate/lead_time for a specific delivery.
SUPPLIERS = [
    {"supplier_id": "SUP-001", "name": "Heartland Frozen Foods Co.", "sku_id": "FOODS_3_586",
     "lead_time_days": 5, "moq": 200, "fill_rate_pct": 97.0, "reliability_score": 4.6},
    {"supplier_id": "SUP-002", "name": "Clearwater Beverage Distributors", "sku_id": "FOODS_3_252",
     "lead_time_days": 6, "moq": 150, "fill_rate_pct": 95.0, "reliability_score": 4.4},
    {"supplier_id": "SUP-003", "name": "Paragon Paper Products", "sku_id": "HOUSEHOLD_1_447",
     "lead_time_days": 10, "moq": 100, "fill_rate_pct": 94.0, "reliability_score": 4.2},
    {"supplier_id": "SUP-004", "name": "Northgate Seasonal Goods", "sku_id": "HOUSEHOLD_1_334",
     "lead_time_days": 12, "moq": 120, "fill_rate_pct": 93.0, "reliability_score": 4.1},
    {"supplier_id": "SUP-005", "name": "Pinecrest Craft Imports", "sku_id": "HOBBIES_1_048",
     "lead_time_days": 21, "moq": 80, "fill_rate_pct": 96.0, "reliability_score": 4.5},
    {"supplier_id": "SUP-006", "name": "Lakeshore Toys & Games Ltd.", "sku_id": "HOBBIES_1_268",
     "lead_time_days": 24, "moq": 60, "fill_rate_pct": 92.0, "reliability_score": 4.0},
]
SUPPLIER_BY_SKU = {s["sku_id"]: s for s in SUPPLIERS}

# ─── Scenario constants ──────────────────────────────────────────────────────
# Real, unmodified seasonal patterns are documented here but require no data
# changes. Promotions and anomalies ARE deliberate adjustments applied below.

PROMO_EVENTS = [
    # The two AC-required scenarios: strong, clearly attributable uplift.
    {"event_id": "PROMO-2015-11-A", "sku_id": "FOODS_3_252", "store_id": "TX_2",
     "start_date": "2015-11-02", "end_date": "2015-11-08",
     "discount_pct": 20, "expected_uplift_pct": 60, "uplift_multiplier": 1.6,
     "scenario": "PROMO-01"},
    {"event_id": "PROMO-2015-11-B", "sku_id": "HOBBIES_1_268", "store_id": "CA_1",
     "start_date": "2015-11-02", "end_date": "2015-11-08",
     "discount_pct": 25, "expected_uplift_pct": 70, "uplift_multiplier": 1.7,
     "scenario": "PROMO-02"},
    # Two minor, non-scenario promos for realistic calendar coverage (no ground
    # truth case attached — they just add ordinary promotional noise).
    {"event_id": "PROMO-2015-12-C", "sku_id": "HOUSEHOLD_1_447", "store_id": "CA_1",
     "start_date": "2015-11-30", "end_date": "2015-12-06",
     "discount_pct": 10, "expected_uplift_pct": 15, "uplift_multiplier": 1.15,
     "scenario": None},
    {"event_id": "PROMO-2015-11-D", "sku_id": "FOODS_3_586", "store_id": "CA_1",
     "start_date": "2015-11-23", "end_date": "2015-11-29",
     "discount_pct": 15, "expected_uplift_pct": 10, "uplift_multiplier": 1.10,
     "scenario": None},
]

ANOMALIES = [
    # Unexplained dip — no promo, no calendar event, no supplier disruption.
    {"id": "ANOMALY-01", "sku_id": "HOBBIES_1_268", "store_id": "CA_1",
     "dates": ["2015-12-09", "2015-12-10", "2015-12-11"], "factor": 0.12, "kind": "dip"},
    # Unexplained spike — same week/SKU as PROMO-01 but a *different store*,
    # with no promo flag, so it cannot be explained by the promo calendar.
    {"id": "ANOMALY-02", "sku_id": "FOODS_3_252", "store_id": "CA_1",
     "dates": ["2015-11-04", "2015-11-05"], "factor": 2.6, "kind": "spike"},
]

# Supplier shipments received during the window, including the 2 disrupted ones.
SUPPLIER_SHIPMENTS = [
    {"shipment_id": "SHP-0001", "supplier_id": "SUP-001", "sku_id": "FOODS_3_586",
     "ordered_date": "2015-11-18", "expected_date": "2015-11-23", "actual_date": "2015-11-23",
     "ordered_qty": 600, "received_qty": 582, "fill_rate_pct": 97.0, "disrupted": False},
    {"shipment_id": "SHP-0002", "supplier_id": "SUP-002", "sku_id": "FOODS_3_252",
     "ordered_date": "2015-11-20", "expected_date": "2015-11-26", "actual_date": "2015-11-27",
     "ordered_qty": 450, "received_qty": 428, "fill_rate_pct": 95.1, "disrupted": False},
    {"shipment_id": "SHP-0003", "supplier_id": "SUP-003", "sku_id": "HOUSEHOLD_1_447",
     "ordered_date": "2015-11-25", "expected_date": "2015-12-05", "actual_date": "2015-12-19",
     "ordered_qty": 350, "received_qty": 329, "fill_rate_pct": 94.0, "disrupted": True,
     "disruption_reason": "Carrier capacity constraint during holiday peak season — 14-day delivery delay",
     "scenario": "SUPPLIER-DELAY-01"},
    {"shipment_id": "SHP-0004", "supplier_id": "SUP-004", "sku_id": "HOUSEHOLD_1_334",
     "ordered_date": "2015-12-02", "expected_date": "2015-12-14", "actual_date": "2015-12-13",
     "ordered_qty": 400, "received_qty": 374, "fill_rate_pct": 93.5, "disrupted": False},
    {"shipment_id": "SHP-0005", "supplier_id": "SUP-005", "sku_id": "HOBBIES_1_048",
     "ordered_date": "2015-12-01", "expected_date": "2015-12-15", "actual_date": "2015-12-15",
     "ordered_qty": 240, "received_qty": 139, "fill_rate_pct": 57.9, "disrupted": True,
     "disruption_reason": "Vendor backorder on imported components — partial shipment (fill rate 57.9%)",
     "scenario": "SUPPLIER-DELAY-02"},
    {"shipment_id": "SHP-0006", "supplier_id": "SUP-006", "sku_id": "HOBBIES_1_268",
     "ordered_date": "2015-11-15", "expected_date": "2015-12-04", "actual_date": "2015-12-03",
     "ordered_qty": 180, "received_qty": 166, "fill_rate_pct": 92.2, "disrupted": False},
]

# ─── Test-case / scenario registry ────────────────────────────────────────────
# The end-to-end test-case scenarios live in `scenarios.py` (the single source of truth,
# shared with generate_normalized_layers.py and build_scenario_folders.py). This script
# only writes the canonical, un-sliced exports under 00_raw/_full_exports/; the per-scenario
# Raw-Layer folders (00_raw/IPF-XXX_<path>/<stage>/) are materialized by build_scenario_folders.py.


# ─── Helpers ──────────────────────────────────────────────────────────────────

def load_extract() -> dict:
    with open(EXTRACT_PATH, encoding="utf-8") as f:
        return json.load(f)


def _emit_csv(out: Path, header: list, rows: list) -> None:
    out.parent.mkdir(parents=True, exist_ok=True)
    with open(out, "w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(header)
        writer.writerows(rows)
    print(f"  {out.relative_to(BASE.parent)}  ({len(rows)} rows)")


def _emit_txt(out: Path, content: str) -> None:
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(content.strip() + "\n", encoding="utf-8")
    print(f"  {out.relative_to(BASE.parent)}")


def write_csv(rel_path: str, header: list, rows: list) -> None:
    """Write a canonical export under 00_raw/_full_exports/<rel_path>."""
    _emit_csv(CANON / rel_path, header, rows)


def write_txt(rel_path: str, content: str) -> None:
    """Write a canonical export under 00_raw/_full_exports/<rel_path>."""
    _emit_txt(CANON / rel_path, content)


def promo_for(sku_id: str, store_id: str, date: str):
    for p in PROMO_EVENTS:
        if p["sku_id"] == sku_id and p["store_id"] == store_id and p["start_date"] <= date <= p["end_date"]:
            return p
    return None


def anomaly_for(sku_id: str, store_id: str, date: str):
    for a in ANOMALIES:
        if a["sku_id"] == sku_id and a["store_id"] == store_id and date in a["dates"]:
            return a
    return None


def week_batches(dates: list) -> list:
    """Split the 77 dates into 11 Monday-Sunday weekly batches."""
    return [dates[i:i + 7] for i in range(0, len(dates), 7)]


# ─── POS transactions (00_raw/_full_exports/pos_transactions/) ──────────────────────────────

def build_pos_transactions(extract: dict) -> None:
    dates = extract["dates"]
    batches = week_batches(dates)

    for batch in batches:
        rows = []
        for s in extract["series"]:
            item_id, store_id, base_price = s["item_id"], s["store_id"], s["sell_price"]
            base_units_by_date = dict(zip(dates, s["unit_sales"]))

            for d in batch:
                units = base_units_by_date[d]
                price = base_price
                promo_ind = 0

                promo = promo_for(item_id, store_id, d)
                if promo:
                    units = round(units * promo["uplift_multiplier"])
                    price = round(base_price * (1 - promo["discount_pct"] / 100), 2)
                    promo_ind = 1

                anomaly = anomaly_for(item_id, store_id, d)
                if anomaly:
                    units = max(0, round(units * anomaly["factor"]))

                net_sales = round(units * price, 2)
                rows.append([d, store_id, item_id, PRODUCT_NAMES[item_id], units, price, net_sales, promo_ind])

        rows.sort(key=lambda r: (r[0], r[1], r[2]))
        start, end = batch[0], batch[-1]
        write_csv(
            f"pos_transactions/pos_export_{start}_to_{end}.csv",
            ["TRANS_DATE", "STORE_ID", "SKU", "PRODUCT_DESC", "UNITS_SOLD", "UNIT_PRICE", "NET_SALES", "PROMO_IND"],
            rows,
        )


# ─── Supplier data (00_raw/_full_exports/supplier_data/) ─────────────────────────────────────

def build_supplier_data() -> None:
    lines = [
        "ERP SUPPLIER MASTER EXTRACT",
        "SYSTEM: VendorHub ERP — Procurement Module",
        "EXTRACT DATE: 2016-01-04",
        "=" * 78,
        "",
    ]
    for s in SUPPLIERS:
        lines += [
            f"SUPPLIER_ID   : {s['supplier_id']}",
            f"NAME          : {s['name']}",
            f"SKU_ID        : {s['sku_id']} ({PRODUCT_NAMES[s['sku_id']]})",
            f"LEAD_TIME_DAYS: {s['lead_time_days']}",
            f"MOQ           : {s['moq']}",
            f"FILL_RATE_PCT : {s['fill_rate_pct']}",
            f"RELIABILITY   : {s['reliability_score']}",
            "-" * 78,
            "",
        ]
    write_txt("supplier_data/supplier_master.txt", "\n".join(lines))

    lines = [
        "ERP SHIPMENT RECEIPT LOG",
        "SYSTEM: VendorHub ERP — Receiving Module",
        "PERIOD: 2015-10-19 to 2016-01-03",
        "=" * 78,
        "",
    ]
    for sh in SUPPLIER_SHIPMENTS:
        lines += [
            f"SHIPMENT_ID  : {sh['shipment_id']}",
            f"SUPPLIER_ID  : {sh['supplier_id']}",
            f"SKU_ID       : {sh['sku_id']} ({PRODUCT_NAMES[sh['sku_id']]})",
            f"ORDERED_DATE : {sh['ordered_date']}",
            f"EXPECTED_DATE: {sh['expected_date']}",
            f"ACTUAL_DATE  : {sh['actual_date']}",
            f"ORDERED_QTY  : {sh['ordered_qty']}",
            f"RECEIVED_QTY : {sh['received_qty']}",
            f"FILL_RATE_PCT: {sh['fill_rate_pct']}",
        ]
        if sh["disrupted"]:
            lines.append(f"*** DISRUPTION: {sh['disruption_reason']} ***")
        lines += ["-" * 78, ""]
    write_txt("supplier_data/supplier_shipments.txt", "\n".join(lines))


# ─── Promotions & price calendar (00_raw/_full_exports/promotions/) ──────────────────────────

def build_promotions() -> None:
    rows = []
    for p in PROMO_EVENTS:
        rows.append([
            p["event_id"], p["sku_id"], p["store_id"], p["start_date"], p["end_date"],
            p["discount_pct"], p["expected_uplift_pct"],
        ])
    write_csv(
        "promotions/promo_calendar.csv",
        ["EVENT_ID", "SKU", "STORE_ID", "START_DATE", "END_DATE", "DISCOUNT_PCT", "EXPECTED_UPLIFT_PCT"],
        rows,
    )


# ─── Inventory snapshots (00_raw/_full_exports/inventory_snapshots/) ─────────────────────────
# Healthy weeks sit at ~1.5 weeks of cover (target_on_hand) with safety_stock at
# ~1 week of cover. The two stockout-risk scenarios are deliberately overridden
# for the specific week(s) the supplier disruption above leaves them exposed —
# this is asserted directly (like the FSI inconsistencies) rather than derived
# from a depletion simulation, so the scenario stays easy to audit.

STOCKOUT_WINDOWS = {
    # SUPPLIER-DELAY-01: shipment due 2015-12-05, doesn't land until 2015-12-19.
    ("HOUSEHOLD_1_447", "TX_2", "2015-12-07"): {"on_hand": 86, "in_transit": 350, "scenario": "STOCKOUT-01"},
    ("HOUSEHOLD_1_447", "TX_2", "2015-12-14"): {"on_hand": 0, "in_transit": 350, "scenario": "STOCKOUT-01"},
    # SUPPLIER-DELAY-02: shipment lands on time (2015-12-15) but only 57.9% filled
    # (139 of 240 units) — the 101-unit shortfall is re-ordered and shown in transit.
    ("HOBBIES_1_048", "CA_1", "2015-12-14"): {"on_hand": 28, "in_transit": 101, "scenario": "STOCKOUT-02"},
}


def build_inventory_snapshots(extract: dict) -> None:
    dates = extract["dates"]
    week_starts = [w[0] for w in week_batches(dates)]

    rows = []
    for s in extract["series"]:
        item_id, store_id = s["item_id"], s["store_id"]
        units_by_date = dict(zip(dates, s["unit_sales"]))
        weekly_totals = [sum(units_by_date[d] for d in w) for w in week_batches(dates)]
        avg_weekly_demand = sum(weekly_totals) / len(weekly_totals)
        safety_stock = round(avg_weekly_demand * 1.0)  # ~1 week of cover
        target_on_hand = round(avg_weekly_demand * 1.5)  # ~1.5 weeks of cover

        for week_start in week_starts:
            override = STOCKOUT_WINDOWS.get((item_id, store_id, week_start))
            on_hand, in_transit = (override["on_hand"], override["in_transit"]) if override else (target_on_hand, 0)
            rows.append([
                week_start, store_id, item_id, on_hand, in_transit, safety_stock,
                "BELOW_SAFETY_STOCK" if on_hand < safety_stock else "OK",
            ])

    rows.sort(key=lambda r: (r[0], r[1], r[2]))
    write_csv(
        "inventory_snapshots/inventory_snapshot.csv",
        ["SNAPSHOT_DATE", "STORE_ID", "SKU", "ON_HAND_UNITS", "IN_TRANSIT_UNITS", "SAFETY_STOCK_UNITS", "STATUS"],
        rows,
    )


# ─── Main ───────────────────────────────────────────────────────────────────

def main():
    extract = load_extract()
    print(f"Source extract: {extract['source']}")
    print(f"Window: {extract['window_start']} .. {extract['window_end']} ({len(extract['dates'])} days)\n")

    print(f"Canonical exports → {CANON.relative_to(BASE.parent)}")
    print("POS transactions:")
    build_pos_transactions(extract)
    print("\nSupplier data:")
    build_supplier_data()
    print("\nPromotions:")
    build_promotions()
    print("\nInventory snapshots:")
    build_inventory_snapshots(extract)

    print(f"\nDone — canonical Raw layer written to {CANON.relative_to(BASE.parent)}")
    print("Next: generate_normalized_layers.py, then build_scenario_folders.py")


if __name__ == "__main__":
    main()
