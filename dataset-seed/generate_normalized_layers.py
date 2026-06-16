#!/usr/bin/env python3
"""
Generate the normalized JSON layers (01-05), and the decision ground truth (07),
for the Retail inventory planning & trend forecasting dataset-seed — FROM the Raw
layer (00_raw/), the reverse direction of the FSI dataset-seed (which renders Raw
FROM Bronze). See dataset-seed/RAW_LAYER.md for why the order is reversed here.

    00_raw/ (system exports)  →  [this script, standing in for the
                                   Signal Ingestion / Feature & Causality agents]
        → 01_pos_transactions/   (extracted + validated POS batches)
        → 02_supplier_data/      (extracted supplier profiles + shipment history)
        → 03_promotions/         (extracted promo calendar + observed uplift)
        → 04_inventory/          (extracted snapshots + computed reorder point)
        → 05_demand_signals/     (feature-engineered time series per SKU)
        → 07_decision_ground_truth/  (expected forecast/replenishment per scenario,
                                       computed from 00_raw/ + 06_policy_rag/ thresholds)

06_policy_rag/ is hand-authored prose (like FSI's), not generated — but every formula
below cites the exact policy ref it implements, so 07/ stays calculable/auditable.

Running this script is idempotent. Requires 00_raw/ to already exist
(run generate_raw_layer.py first).
"""

import csv
import json
import statistics
from pathlib import Path

from generate_raw_layer import (
    PRODUCT_NAMES,
    SUPPLIER_BY_SKU,
    PROMO_EVENTS,
    ANOMALIES,
    week_batches,
    load_extract,
)

BASE = Path(__file__).resolve().parent
RAW = BASE / "00_raw"

# Policy constants — must match dataset-seed/06_policy_rag/*.txt exactly.
SAFETY_STOCK_WEEKS = 1.0       # SL-100
TARGET_ON_HAND_WEEKS = 1.5     # SL-100
BUDGET_CAP_MULTIPLIER = 3.0    # BG-300
FILL_RATE_DISRUPTION_PCT = 70  # SP-400
HOLIDAY_EVENT_TYPES = {"National", "Cultural", "Religious"}  # SN-500


def cat_of(sku_id: str) -> str:
    return sku_id.split("_")[0]


def write_json(rel_path: str, obj: dict, counter: list) -> None:
    out = BASE / rel_path
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(obj, indent=2) + "\n", encoding="utf-8")
    counter.append(out)


# ─── Parse 00_raw/ ────────────────────────────────────────────────────────────

def parse_pos() -> dict:
    """(sku, store) -> {date: {units, price, revenue, promo}} across all weekly batches."""
    data: dict = {}
    for fp in sorted((RAW / "pos_transactions").glob("pos_export_*.csv")):
        with open(fp, encoding="utf-8") as f:
            for row in csv.DictReader(f):
                key = (row["SKU"], row["STORE_ID"])
                data.setdefault(key, {})[row["TRANS_DATE"]] = {
                    "units": int(row["UNITS_SOLD"]),
                    "price": float(row["UNIT_PRICE"]),
                    "revenue": float(row["NET_SALES"]),
                    "promo": row["PROMO_IND"] == "1",
                }
    return data


def _parse_fields(block: str) -> dict:
    """Parse 'KEY : value' lines (any amount of padding) into a {key.strip(): value.strip()} dict."""
    fields = {}
    for line in block.strip().splitlines():
        if ":" not in line or line.startswith("***"):
            continue
        key, value = line.split(":", 1)
        fields[key.strip()] = value.strip()
    return fields


def parse_supplier_master() -> list:
    blocks = (RAW / "supplier_data" / "supplier_master.txt").read_text(encoding="utf-8").split("-" * 78)
    suppliers = []
    for block in blocks:
        if "SUPPLIER_ID" not in block:
            continue
        fields = _parse_fields(block)
        suppliers.append({
            "supplier_id": fields["SUPPLIER_ID"],
            "name": fields["NAME"],
            "sku_id": fields["SKU_ID"].split(" (")[0],
            "lead_time_days": int(fields["LEAD_TIME_DAYS"]),
            "moq": int(fields["MOQ"]),
            "fill_rate_pct": float(fields["FILL_RATE_PCT"]),
            "reliability_score": float(fields["RELIABILITY"]),
        })
    return suppliers


def parse_supplier_shipments() -> list:
    text = (RAW / "supplier_data" / "supplier_shipments.txt").read_text(encoding="utf-8")
    blocks = text.split("-" * 78)
    shipments = []
    for block in blocks:
        if "SHIPMENT_ID" not in block:
            continue
        lines = block.strip().splitlines()
        disrupted = any(l.startswith("*** DISRUPTION") for l in lines)
        reason = next((l.split(": ", 1)[1].rstrip(" *") for l in lines if l.startswith("*** DISRUPTION")), None)
        fields = _parse_fields(block)
        shipments.append({
            "shipment_id": fields["SHIPMENT_ID"],
            "supplier_id": fields["SUPPLIER_ID"],
            "sku_id": fields["SKU_ID"].split(" (")[0],
            "ordered_date": fields["ORDERED_DATE"],
            "expected_date": fields["EXPECTED_DATE"],
            "actual_date": fields["ACTUAL_DATE"],
            "ordered_qty": int(fields["ORDERED_QTY"]),
            "received_qty": int(fields["RECEIVED_QTY"]),
            "fill_rate_pct": float(fields["FILL_RATE_PCT"]),
            "disrupted": disrupted,
            "disruption_reason": reason,
        })
    return shipments


def parse_promo_calendar() -> list:
    with open(RAW / "promotions" / "promo_calendar.csv", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def parse_inventory() -> list:
    with open(RAW / "inventory_snapshots" / "inventory_snapshot.csv", encoding="utf-8") as f:
        return list(csv.DictReader(f))


# ─── 01_pos_transactions/ ──────────────────────────────────────────────────────

def build_pos_transactions(pos: dict, dates: list, counter: list) -> None:
    for (sku, store), by_date in sorted(pos.items()):
        for batch in week_batches(dates):
            week_start, week_end = batch[0], batch[-1]
            daily = [
                {
                    "date": d,
                    "units_sold": by_date[d]["units"],
                    "unit_price": by_date[d]["price"],
                    "revenue": by_date[d]["revenue"],
                    "promo_flag": by_date[d]["promo"],
                }
                for d in batch
            ]
            total_units = sum(r["units_sold"] for r in daily)
            total_revenue = round(sum(r["revenue"] for r in daily), 2)
            doc = {
                "document_id": f"POS-{sku}-{store}-{week_start}",
                "document_type": "pos_transaction_batch",
                "document_date": week_start,
                "source_system": "pos_export",
                "sku_id": sku,
                "store_id": store,
                "category": cat_of(sku),
                "product_desc": PRODUCT_NAMES[sku],
                "batch_week_start": week_start,
                "batch_week_end": week_end,
                "daily_records": daily,
                "weekly_summary": {
                    "total_units_sold": total_units,
                    "total_revenue": total_revenue,
                    "promo_days": sum(1 for r in daily if r["promo_flag"]),
                    "avg_unit_price": round(statistics.mean(r["unit_price"] for r in daily), 2),
                },
            }
            write_json(f"01_pos_transactions/POS-{sku}-{store}-{week_start}.json", doc, counter)


# ─── 02_supplier_data/ ──────────────────────────────────────────────────────────

def build_supplier_data(suppliers: list, shipments: list, window_end: str, counter: list) -> None:
    for s in suppliers:
        sku_shipments = [sh for sh in shipments if sh["supplier_id"] == s["supplier_id"]]
        doc = {
            "document_id": s["supplier_id"],
            "document_type": "supplier_profile",
            "document_date": window_end,
            "source_system": "vendorhub_erp",
            "supplier_id": s["supplier_id"],
            "name": s["name"],
            "sku_ids": [s["sku_id"]],
            "lead_time_days": s["lead_time_days"],
            "moq": s["moq"],
            "fill_rate_pct": s["fill_rate_pct"],
            "reliability_score": s["reliability_score"],
            "performance_flag": "review_required" if s["reliability_score"] < 4.0 else "ok",
            "shipments": [
                {
                    "shipment_id": sh["shipment_id"],
                    "sku_id": sh["sku_id"],
                    "ordered_date": sh["ordered_date"],
                    "expected_date": sh["expected_date"],
                    "actual_date": sh["actual_date"],
                    "ordered_qty": sh["ordered_qty"],
                    "received_qty": sh["received_qty"],
                    "fill_rate_pct": sh["fill_rate_pct"],
                    "disrupted": sh["disrupted"],
                    "disruption_reason": sh["disruption_reason"],
                }
                for sh in sku_shipments
            ],
        }
        write_json(f"02_supplier_data/{s['supplier_id']}.json", doc, counter)


# ─── 03_promotions/ ─────────────────────────────────────────────────────────────

def build_promotions(promos: list, pos: dict, dates: list, counter: list) -> None:
    for p in promos:
        sku, store = p["SKU"], p["STORE_ID"]
        by_date = pos[(sku, store)]
        promo_dates = [d for d in dates if p["START_DATE"] <= d <= p["END_DATE"]]
        promo_units = sum(by_date[d]["units"] for d in promo_dates)

        # Baseline = the 14 days immediately before the promo (or after, if the promo
        # starts in the first 2 weeks of the window) — NOT "all other days", which would
        # pull in the Thanksgiving/Christmas peak weeks and understate the true uplift
        # for a promo run in a quiet week.
        idx = dates.index(promo_dates[0])
        if idx >= 14:
            baseline_dates = dates[idx - 14:idx]
        else:
            end_idx = dates.index(promo_dates[-1])
            baseline_dates = dates[end_idx + 1:end_idx + 15]
        baseline_avg_daily = statistics.mean(by_date[d]["units"] for d in baseline_dates)
        baseline_window_total = round(baseline_avg_daily * len(promo_dates), 1)
        observed_uplift_pct = round((promo_units / baseline_window_total - 1) * 100, 1) if baseline_window_total else None

        doc = {
            "document_id": p["EVENT_ID"],
            "document_type": "promotion_event",
            "document_date": p["START_DATE"],
            "source_system": "pricing_calendar_system",
            "event_id": p["EVENT_ID"],
            "sku_id": sku,
            "store_id": store,
            "start_date": p["START_DATE"],
            "end_date": p["END_DATE"],
            "discount_pct": int(p["DISCOUNT_PCT"]),
            "expected_uplift_pct": int(p["EXPECTED_UPLIFT_PCT"]),
            "observed_units_in_window": promo_units,
            "baseline_units_in_window": baseline_window_total,
            "observed_uplift_pct": observed_uplift_pct,
        }
        write_json(f"03_promotions/{p['EVENT_ID']}.json", doc, counter)


# ─── 04_inventory/ ──────────────────────────────────────────────────────────────

def build_inventory(inventory_rows: list, counter: list) -> None:
    # Target on-hand baseline per (sku, store) = the actual healthy on-hand value seen
    # in an OK snapshot (not re-derived from safety_stock * 1.5, to avoid the
    # double-rounding drift described in build_ground_truth).
    target_by_pair = {
        (r["SKU"], r["STORE_ID"]): int(r["ON_HAND_UNITS"])
        for r in inventory_rows if r["STATUS"] == "OK"
    }

    for row in inventory_rows:
        sku, store = row["SKU"], row["STORE_ID"]
        safety_stock = int(row["SAFETY_STOCK_UNITS"])
        lead_time_days = SUPPLIER_BY_SKU[sku]["lead_time_days"]
        # avg_weekly_demand == safety_stock_units by construction (SAFETY_STOCK_WEEKS == 1.0,
        # see generate_raw_layer.py build_inventory_snapshots) — RP-200's reorder point formula:
        reorder_point = safety_stock + round(safety_stock * lead_time_days / 7)
        on_hand = int(row["ON_HAND_UNITS"])
        target_on_hand = target_by_pair[(sku, store)]
        # This dataset's actual operative reorder trigger is `status` (BELOW_SAFETY_STOCK),
        # not a textbook lead-time reorder point — target_on_hand here is a flat 1.5-week
        # cover policy, not sized per SKU's lead time, so RP-200's reorder_point can land
        # above target_on_hand for longer-lead-time SKUs. Rather than a boolean that would
        # read as uniformly (and suspiciously) true, expose the gap as a number: it is small
        # for short-lead FOODS SKUs and large for the 21-24 day import HOBBIES SKUs — which
        # is precisely why a single missed/short HOBBIES delivery becomes a stockout while
        # the same disruption on a FOODS SKU would not (see SUPPLIER-DELAY-02/STOCKOUT-02
        # vs. the FOODS SKUs, which carry no stockout scenario, in RAW_LAYER.md).
        lead_time_cover_gap_units = max(0, reorder_point - target_on_hand)

        doc = {
            "document_id": f"INV-{sku}-{store}-{row['SNAPSHOT_DATE']}",
            "document_type": "inventory_snapshot",
            "document_date": row["SNAPSHOT_DATE"],
            "source_system": "inventory_management_system",
            "sku_id": sku,
            "store_id": store,
            "on_hand_units": on_hand,
            "in_transit_units": int(row["IN_TRANSIT_UNITS"]),
            "safety_stock_units": safety_stock,
            "target_on_hand_units": target_on_hand,
            "reorder_point_units": reorder_point,
            "lead_time_cover_gap_units": lead_time_cover_gap_units,
            "status": row["STATUS"],
        }
        write_json(f"04_inventory/INV-{sku}-{store}-{row['SNAPSHOT_DATE']}.json", doc, counter)


# ─── 05_demand_signals/ ─────────────────────────────────────────────────────────

def build_demand_signals(pos: dict, dates: list, calendar: list, inventory_rows: list, counter: list) -> None:
    by_date_cal = {c["date"]: c for c in calendar}
    safety_stock_by_pair = {(r["SKU"], r["STORE_ID"]): int(r["SAFETY_STOCK_UNITS"]) for r in inventory_rows}
    batches = week_batches(dates)

    skus = sorted({sku for sku, _ in pos})
    for sku in skus:
        stores = sorted(store for (s, store) in pos if s == sku)
        store_blocks = {}
        for store in stores:
            by_date = pos[(sku, store)]
            week_starts = [b[0] for b in batches]
            weekly_units = [sum(by_date[d]["units"] for d in b) for b in batches]

            rolling = []
            for i in range(len(weekly_units)):
                window = weekly_units[max(0, i - 2):i + 1]
                rolling.append(round(statistics.mean(window), 1))

            pct_change = [None]
            for i in range(1, len(weekly_units)):
                prev = weekly_units[i - 1]
                pct_change.append(round((weekly_units[i] / prev - 1) * 100, 1) if prev else None)

            daily_units = [by_date[d]["units"] for d in dates]
            mean_daily, stdev_daily = statistics.mean(daily_units), statistics.pstdev(daily_units)
            statistical_anomaly_weeks = [
                b[0] for b in batches
                if any(stdev_daily and abs(by_date[d]["units"] - mean_daily) > 2.5 * stdev_daily for d in b)
            ]

            promo_weeks = sorted({
                b[0] for b in batches for p in PROMO_EVENTS
                if p["sku_id"] == sku and p["store_id"] == store
                and any(p["start_date"] <= d <= p["end_date"] for d in b)
            })
            holiday_weeks = sorted({
                b[0] for b in batches
                if any(by_date_cal[d]["event_type_1"] in HOLIDAY_EVENT_TYPES
                       or by_date_cal[d]["event_type_2"] in HOLIDAY_EVENT_TYPES for d in b)
            })
            scenario_refs = sorted({
                a["id"] for a in ANOMALIES if a["sku_id"] == sku and a["store_id"] == store
            } | {
                p["scenario"] for p in PROMO_EVENTS
                if p["sku_id"] == sku and p["store_id"] == store and p["scenario"]
            })

            store_blocks[store] = {
                "week_start_dates": week_starts,
                "weekly_units": weekly_units,
                "rolling_3wk_avg": rolling,
                "pct_change_vs_prior_week": pct_change,
                "avg_weekly_demand": safety_stock_by_pair[(sku, store)],
                "statistical_anomaly_weeks": statistical_anomaly_weeks,
                "promo_weeks": promo_weeks,
                "holiday_weeks": holiday_weeks,
                "scenario_refs": scenario_refs,
            }

        doc = {
            "document_id": f"DMD-{sku}",
            "document_type": "demand_signal",
            "document_date": dates[-1],
            "source_system": "signal_ingestion_agent",
            "sku_id": sku,
            "category": cat_of(sku),
            "product_desc": PRODUCT_NAMES[sku],
            "stores": store_blocks,
        }
        write_json(f"05_demand_signals/DMD-{sku}.json", doc, counter)


# ─── 07_decision_ground_truth/ ──────────────────────────────────────────────────

SCENARIOS = [
    {"id": "SEASONAL-01", "type": "seasonal_trend", "sku": "FOODS_3_586", "stores": ["CA_1", "TX_2"],
     "weeks": ["2015-11-23", "2015-12-14"],
     "reason": "seasonal_holiday_demand_peak", "refs": ["SN-500", "SL-100"],
     "summary": "Real Thanksgiving and pre-Christmas demand peaks at both stores; forecast off the holiday week's own historical level (SN-500), not the trailing average."},
    {"id": "SEASONAL-02", "type": "seasonal_trend", "sku": "HOUSEHOLD_1_334", "stores": ["TX_2"],
     "weeks": ["2015-12-21"],
     "reason": "seasonal_ramp_into_holiday", "refs": ["SN-500", "SL-100"],
     "summary": "Real, steep ramp into Christmas week at TX_2 (not present at CA_1 — regional variation); forecast off the holiday week's own historical level."},
    {"id": "PROMO-01", "type": "promotion_demand_spike", "sku": "FOODS_3_252", "stores": ["TX_2"],
     "weeks": ["2015-11-02"],
     "reason": "promotional_demand_uplift", "refs": ["SN-510", "RP-200", "BG-300"],
     "summary": "20% discount, declared expected_uplift_pct=60; forecast = baseline x (1 + uplift) per SN-510."},
    {"id": "PROMO-02", "type": "promotion_demand_spike", "sku": "HOBBIES_1_268", "stores": ["CA_1"],
     "weeks": ["2015-11-02"],
     "reason": "promotional_demand_uplift", "refs": ["SN-510", "RP-200", "BG-300"],
     "summary": "25% discount, declared expected_uplift_pct=70, same week as PROMO-01 but a different SKU/store."},
    {"id": "SUPPLIER-DELAY-01", "type": "supplier_delay", "sku": "HOUSEHOLD_1_447", "stores": ["TX_2"],
     "weeks": ["2015-12-07", "2015-12-14"], "supplier_id": "SUP-003", "shipment_id": "SHP-0003",
     "reason": "supplier_lead_time_disruption", "refs": ["SP-410", "RP-200", "SL-100"],
     "summary": "14-day late delivery (expected 2015-12-05, actual 2015-12-19); timing risk per SP-410, not a quantity shortfall — qty already in transit covers the gap."},
    {"id": "SUPPLIER-DELAY-02", "type": "supplier_delay", "sku": "HOBBIES_1_048", "stores": ["CA_1"],
     "weeks": ["2015-12-14"], "supplier_id": "SUP-005", "shipment_id": "SHP-0005",
     "reason": "supplier_fill_rate_disruption", "refs": ["SP-400", "RP-210"],
     "summary": "On-time but only 57.9% filled (139 of 240 units) — below the 70% SP-400 threshold; follow-up order required for the shortfall."},
    {"id": "STOCKOUT-01", "type": "stockout_risk", "sku": "HOUSEHOLD_1_447", "stores": ["TX_2"],
     "weeks": ["2015-12-07", "2015-12-14"], "caused_by": "SUPPLIER-DELAY-01",
     "reason": "stockout_risk_pending_delayed_shipment", "refs": ["SL-100", "RP-200", "SP-410"],
     "summary": "On-hand falls to 86 then 0, both below the 151-unit safety stock, right before TX_2's Christmas-week demand — caused by SUPPLIER-DELAY-01."},
    {"id": "STOCKOUT-02", "type": "stockout_risk", "sku": "HOBBIES_1_048", "stores": ["CA_1"],
     "weeks": ["2015-12-14"], "caused_by": "SUPPLIER-DELAY-02",
     "reason": "stockout_risk_after_fill_rate_shortfall", "refs": ["SL-100", "RP-200", "RP-210"],
     "summary": "On-hand falls to 28, below the 103-unit safety stock — caused by SUPPLIER-DELAY-02's partial shipment."},
    {"id": "ANOMALY-01", "type": "demand_anomaly", "sku": "HOBBIES_1_268", "stores": ["CA_1"],
     "weeks": ["2015-12-07"], "anomaly": True,
     "reason": "unexplained_demand_dip", "refs": [],
     "summary": "Units drop to 1, 1, 3 on 2015-12-09/10/11 with no promo flag, calendar event, or supplier disruption attached — flagged for investigation, not a supply-side response."},
    {"id": "ANOMALY-02", "type": "demand_anomaly", "sku": "FOODS_3_252", "stores": ["CA_1"],
     "weeks": ["2015-11-02"], "anomaly": True,
     "reason": "unexplained_demand_spike", "refs": [],
     "summary": "Units jump to 36, 73 on 2015-11-04/05 with promo_flag=0 — same week as PROMO-01 but a different store, so it cannot be explained by the promo calendar."},
]


def build_ground_truth(pos: dict, inventory_rows: list, suppliers: list, shipments: list, promos: list, counter: list) -> list:
    inv_by_key = {(r["SKU"], r["STORE_ID"], r["SNAPSHOT_DATE"]): r for r in inventory_rows}
    shipments_by_id = {sh["shipment_id"]: sh for sh in shipments}
    moq_by_sku = {s["sku_id"]: s["moq"] for s in suppliers}
    promo_by_key = {(p["SKU"], p["STORE_ID"]): p for p in promos}
    csv_rows = []

    for sc in SCENARIOS:
        sku, stores = sc["sku"], sc["stores"]
        store = stores[0]
        avg_weekly_demand = int(inv_by_key[(sku, store, sc["weeks"][0])]["SAFETY_STOCK_UNITS"])
        # Read target_on_hand from an actual healthy snapshot rather than re-deriving
        # avg_weekly_demand * 1.5 here — re-deriving it from the already-rounded
        # avg_weekly_demand double-rounds and can drift by a unit from what 04_inventory/
        # actually shows (e.g. round(151 * 1.5) = 226 vs the real 227 on-hand at target).
        target_on_hand = next(
            int(r["ON_HAND_UNITS"]) for r in inventory_rows
            if r["SKU"] == sku and r["STORE_ID"] == store and r["STATUS"] == "OK"
        )

        # Default expected forecast = the undisrupted baseline for every affected week,
        # so even supply-side/anomaly scenarios report "what we'd have expected" for
        # comparison against what actually happened. Scenario types with their own
        # forecasting rule (seasonal/promo) override this below.
        expected_forecast = {f"{store}|{week}": avg_weekly_demand for week in sc["weeks"]}
        recommended_qty, expedite, shortfall_units = 0, False, 0

        if sc["type"] == "seasonal_trend":
            for store_i in stores:
                for week in sc["weeks"]:
                    expected_forecast[f"{store_i}|{week}"] = pos_week_total(pos, sku, store_i, week)
            recommended_qty = max(expected_forecast.values())

        elif sc["type"] == "promotion_demand_spike":
            promo = promo_by_key[(sku, store)]
            forecast = round(avg_weekly_demand * (1 + int(promo["EXPECTED_UPLIFT_PCT"]) / 100))
            expected_forecast[f"{store}|{sc['weeks'][0]}"] = forecast
            cap = round(BUDGET_CAP_MULTIPLIER * avg_weekly_demand)
            recommended_qty = min(forecast, cap)

        elif sc["type"] == "supplier_delay":
            sh = shipments_by_id[sc["shipment_id"]]
            shortfall_units = max(0, sh["ordered_qty"] - sh["received_qty"])
            moq = moq_by_sku[sku]
            recommended_qty = max(moq, shortfall_units) if shortfall_units else 0
            expedite = sh["fill_rate_pct"] >= FILL_RATE_DISRUPTION_PCT  # late but not short -> timing risk

        elif sc["type"] == "stockout_risk":
            for week in sc["weeks"]:
                row = inv_by_key[(sku, store, week)]
                gap = target_on_hand - int(row["ON_HAND_UNITS"]) - int(row["IN_TRANSIT_UNITS"])
                shortfall_units = max(shortfall_units, max(0, gap))
            moq = moq_by_sku[sku]
            recommended_qty = max(moq, shortfall_units) if shortfall_units else 0
            expedite = shortfall_units == 0  # gap already covered by an in-transit shipment -> it's a timing risk

        elif sc["type"] == "demand_anomaly":
            recommended_qty = 0

        doc = {
            "document_id": sc["id"],
            "document_type": "decision_ground_truth",
            "document_date": sc["weeks"][0],
            "source_system": "replenishment_ground_truth",
            "scenario_id": sc["id"],
            "scenario_type": sc["type"],
            "sku_id": sku,
            "store_ids": stores,
            "affected_weeks": sc["weeks"],
            "avg_weekly_demand": avg_weekly_demand,
            "target_on_hand_units": target_on_hand,
            "expected_forecast_units_per_week": expected_forecast,
            "shortfall_units": shortfall_units,
            "recommended_replenishment_order_qty": recommended_qty,
            "expedite_required": expedite,
            "anomaly_flag": sc.get("anomaly", False),
            "primary_reason": sc["reason"],
            "top_policy_refs": sc["refs"],
            "summary_explanation": sc["summary"],
        }
        write_json(f"07_decision_ground_truth/{sc['id']}.json", doc, counter)
        csv_rows.append([
            sc["id"], sku, ",".join(stores), sc["type"], doc["recommended_replenishment_order_qty"],
            doc["anomaly_flag"], doc["expedite_required"], sc["reason"],
        ])

    out = BASE / "07_decision_ground_truth" / "ground_truth.csv"
    with open(out, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["scenario_id", "sku_id", "store_ids", "scenario_type", "recommended_replenishment_order_qty",
                    "anomaly_flag", "expedite_required", "primary_reason"])
        w.writerows(csv_rows)
    print(f"  {out.relative_to(BASE.parent)}")
    return csv_rows


def pos_week_total(pos: dict, sku: str, store: str, week_start: str) -> int:
    by_date = pos[(sku, store)]
    batch = next(b for b in week_batches(sorted(by_date)) if b[0] == week_start)
    return sum(by_date[d]["units"] for d in batch)


# ─── dataset_summary.json ───────────────────────────────────────────────────────

def build_dataset_summary(counts: dict, csv_rows: list, window_start: str, window_end: str) -> None:
    scenario_type_counts: dict = {}
    for row in csv_rows:
        scenario_type_counts[row[3]] = scenario_type_counts.get(row[3], 0) + 1

    summary = {
        "dataset_name": "retail-agentic-inventory-planning",
        "window_start": window_start,
        "window_end": window_end,
        "sku_count": 6,
        "store_count": 2,
        "category_count": 3,
        "document_counts": counts,
        "scenario_coverage": scenario_type_counts,
    }
    out = BASE / "dataset_summary.json"
    out.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(f"  {out.relative_to(BASE.parent)}")


# ─── Main ───────────────────────────────────────────────────────────────────────

def main():
    extract = load_extract()
    dates, calendar = extract["dates"], extract["calendar"]

    pos = parse_pos()
    suppliers = parse_supplier_master()
    shipments = parse_supplier_shipments()
    promos = parse_promo_calendar()
    inventory_rows = parse_inventory()

    counts = {}
    for name, fn in [
        ("pos_transaction_batch", lambda c: build_pos_transactions(pos, dates, c)),
        ("supplier_profile", lambda c: build_supplier_data(suppliers, shipments, dates[-1], c)),
        ("promotion_event", lambda c: build_promotions(promos, pos, dates, c)),
        ("inventory_snapshot", lambda c: build_inventory(inventory_rows, c)),
        ("demand_signal", lambda c: build_demand_signals(pos, dates, calendar, inventory_rows, c)),
    ]:
        print(f"{name}:")
        c: list = []
        fn(c)
        counts[name] = len(c)
        print(f"  -> {len(c)} files")

    print("decision_ground_truth:")
    c = []
    csv_rows = build_ground_truth(pos, inventory_rows, suppliers, shipments, promos, c)
    counts["decision_ground_truth"] = len(c)
    print(f"  -> {len(c)} files")

    build_dataset_summary(counts, csv_rows, dates[0], dates[-1])
    print(f"\nDone — normalized layers + ground truth written under {BASE}")


if __name__ == "__main__":
    main()
