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
from scenarios import SCENARIOS, scenario_folder, stage_folder

BASE = Path(__file__).resolve().parent
RAW = BASE / "00_raw"
# Read the canonical, un-sliced exports (the per-scenario folders are demo slices).
CANON = RAW / "_full_exports"

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
    for fp in sorted((CANON / "pos_transactions").glob("pos_export_*.csv")):
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
    blocks = (CANON / "supplier_data" / "supplier_master.txt").read_text(encoding="utf-8").split("-" * 78)
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
    text = (CANON / "supplier_data" / "supplier_shipments.txt").read_text(encoding="utf-8")
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
    with open(CANON / "promotions" / "promo_calendar.csv", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def parse_inventory() -> list:
    with open(CANON / "inventory_snapshots" / "inventory_snapshot.csv", encoding="utf-8") as f:
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
            "source_system": "feature_causality_agent",
            "sku_id": sku,
            "category": cat_of(sku),
            "product_desc": PRODUCT_NAMES[sku],
            "stores": store_blocks,
        }
        write_json(f"05_demand_signals/DMD-{sku}.json", doc, counter)


# ─── 07_decision_ground_truth/ ──────────────────────────────────────────────────

SCENARIO_SOURCE_SYSTEM = "inventory_planning_ground_truth"


def _dmd_block(sku: str, store: str) -> dict:
    """Read the already-written Feature & Causality output for this SKU/store."""
    data = json.loads((BASE / "05_demand_signals" / f"DMD-{sku}.json").read_text(encoding="utf-8"))
    return data["stores"][store]


def _observed_uplift(event_id):
    if not event_id:
        return None
    for fp in (BASE / "03_promotions").glob("*.json"):
        p = json.loads(fp.read_text(encoding="utf-8"))
        if p.get("event_id") == event_id:
            return p.get("observed_uplift_pct")
    return None


def compute_decision(anchor, pos, inv_by_key, inventory_rows, shipments_by_id,
                     moq_by_sku, promo_by_key) -> dict:
    """Reference computation of the forecast + replenishment + budget outcome for one
    scenario anchor — every number derived from 00_raw/ + 06_policy_rag/ thresholds."""
    sku, stores = anchor["sku"], anchor["stores"]
    store = stores[0]
    weeks = anchor["weeks"]
    avg_weekly_demand = int(inv_by_key[(sku, store, weeks[0])]["SAFETY_STOCK_UNITS"])
    target_on_hand = next(
        int(r["ON_HAND_UNITS"]) for r in inventory_rows
        if r["SKU"] == sku and r["STORE_ID"] == store and r["STATUS"] == "OK"
    )
    cap = round(BUDGET_CAP_MULTIPLIER * avg_weekly_demand)

    expected_forecast = {f"{store}|{week}": avg_weekly_demand for week in weeks}
    proposed_qty, shortfall_units, expedite = 0, 0, False
    binding_constraint = "none"

    if anchor["type"] == "seasonal_trend":
        for store_i in stores:
            for week in weeks:
                expected_forecast[f"{store_i}|{week}"] = pos_week_total(pos, sku, store_i, week)
        proposed_qty = max(expected_forecast.values())

    elif anchor["type"] == "promotion_demand_spike":
        promo = promo_by_key[(sku, store)]
        forecast = round(avg_weekly_demand * (1 + int(promo["EXPECTED_UPLIFT_PCT"]) / 100))
        expected_forecast[f"{store}|{weeks[0]}"] = forecast
        proposed_qty = forecast

    elif anchor["type"] == "stockout_risk":
        for week in weeks:
            row = inv_by_key[(sku, store, week)]
            gap = target_on_hand - int(row["ON_HAND_UNITS"]) - int(row["IN_TRANSIT_UNITS"])
            shortfall_units = max(shortfall_units, max(0, gap))
        moq = moq_by_sku[sku]
        if shortfall_units > 0:
            proposed_qty = max(moq, shortfall_units)
            binding_constraint = "MOQ" if moq > shortfall_units else "RP-200"
        else:
            expedite = True                # gap already covered by an in-transit shipment
            binding_constraint = "SL-100"  # service-level timing risk, not a quantity order

    elif anchor["type"] == "demand_anomaly":
        proposed_qty = 0

    approved_qty = min(proposed_qty, cap)
    if approved_qty < proposed_qty:
        binding_constraint = "BG-300"

    return {
        "avg_weekly_demand": avg_weekly_demand,
        "target_on_hand_units": target_on_hand,
        "expected_forecast_units_per_week": expected_forecast,
        "shortfall_units": shortfall_units,
        "proposed_order_qty": proposed_qty,
        "approved_order_qty": approved_qty,
        "budget_cap_units": cap,
        "binding_constraint": binding_constraint,
        "expedite_required": expedite,
        "anomaly_flag": anchor.get("anomaly", False),
    }


def _stage_expected_output(stage_name, anchor, dec, has_promo) -> dict:
    """Per-stage expected_output — qualitative for ingestion/features, numeric downstream."""
    sku, store = anchor["sku"], anchor["stores"][0]
    if stage_name == "signal_ingestion":
        layers = ["01_pos_transactions", "04_inventory"]
        if "supplier_id" in anchor:
            layers.append("02_supplier_data")
        if has_promo:
            layers.append("03_promotions")
        return {"decision": "signals_validated",
                "expected_output": {"normalized_layers": sorted(layers),
                                    "quality_status": "validated"}}
    if stage_name == "feature_causality":
        blk = _dmd_block(sku, store)
        return {"decision": "features_built",
                "expected_output": {"avg_weekly_demand": blk["avg_weekly_demand"],
                                    "statistical_anomaly_weeks": blk["statistical_anomaly_weeks"],
                                    "promo_weeks": blk["promo_weeks"],
                                    "holiday_weeks": blk["holiday_weeks"]}}
    if stage_name == "forecasting":
        decision = "anomaly_flagged_for_review" if dec["anomaly_flag"] else "forecast_produced"
        return {"decision": decision,
                "expected_output": {"expected_forecast_units_per_week": dec["expected_forecast_units_per_week"],
                                    "anomaly_flag": dec["anomaly_flag"]}}
    if stage_name == "replenishment_allocation":
        if dec["expedite_required"]:
            decision = "expedite_flagged_no_new_order"
        elif dec["proposed_order_qty"] > 0:
            decision = "reorder_recommended"
        else:
            decision = "no_supply_action"
        return {"decision": decision,
                "expected_output": {"proposed_order_qty": dec["proposed_order_qty"],
                                    "shortfall_units": dec["shortfall_units"],
                                    "target_on_hand_units": dec["target_on_hand_units"],
                                    "expedite_required": dec["expedite_required"]}}
    # planner_copilot — decision is set to the scenario final_outcome by the caller
    return {"decision": None,
            "expected_output": {"approved_order_qty": dec["approved_order_qty"],
                                "budget_cap_units": dec["budget_cap_units"],
                                "binding_constraint": dec["binding_constraint"]}}


def build_scenario_ground_truth(pos, inventory_rows, suppliers, shipments, promos, counter) -> list:
    """Emit one e2e ground-truth rollup per scenario (IPF-XXX.json), replacing the
    previous per-scenario-type decision files. Each rollup describes the full workflow
    path: the orchestrator request, the ordered agent stages (each with the structured
    agent_input, the decision/expected_output it would produce, and the HITL gate), and
    the final outcome. The scenario set is defined once in scenarios.py."""
    gt_dir = BASE / "07_decision_ground_truth"
    for stale in list(gt_dir.glob("*.json")):  # drop legacy per-type cases
        stale.unlink()

    inv_by_key = {(r["SKU"], r["STORE_ID"], r["SNAPSHOT_DATE"]): r for r in inventory_rows}
    shipments_by_id = {sh["shipment_id"]: sh for sh in shipments}
    moq_by_sku = {s["sku_id"]: s["moq"] for s in suppliers}
    promo_by_key = {(p["SKU"], p["STORE_ID"]): p for p in promos}

    csv_rows = []
    for scenario in SCENARIOS:
        anchor = scenario["anchor"]
        promo_events = scenario["raw_slice"].get("promo_events") or []
        has_promo = bool(promo_events)
        dec = compute_decision(anchor, pos, inv_by_key, inventory_rows,
                               shipments_by_id, moq_by_sku, promo_by_key)

        stages_out = []
        for order, stage in enumerate(scenario["stages"], start=1):
            name = stage["stage"]
            so = _stage_expected_output(name, anchor, dec, has_promo)
            if name == "feature_causality" and has_promo:
                so["expected_output"]["observed_uplift_pct"] = _observed_uplift(promo_events[0])
            decision = so["decision"]
            if name == "planner_copilot":
                decision = scenario["final_outcome"]
                so["expected_output"]["required_human_review"] = scenario["required_human_review"]
                so["expected_output"]["final_outcome"] = scenario["final_outcome"]
            stages_out.append({
                "order": order,
                "stage": name,
                "agent": stage["agent"],
                "agent_input": stage["agent_input"],
                "gate": stage["gate"],
                "policy_refs": stage["policy_refs"],
                "decision": decision,
                "expected_output": so["expected_output"],
                "raw_layer_folder": f"00_raw/{scenario_folder(scenario)}/{stage_folder(stage)}/",
            })

        rollup = {
            "document_id": scenario["scenario_id"],
            "document_type": "decision_ground_truth",
            "document_date": anchor["weeks"][0],
            "source_system": SCENARIO_SOURCE_SYSTEM,
            "scenario_id": scenario["scenario_id"],
            "scenario_kind": "e2e_workflow_path",
            "scenario_type": anchor["type"],
            "path": scenario["path"],
            "title": scenario["title"],
            "scenario_folder": scenario_folder(scenario),
            "sku_id": anchor["sku"],
            "store_ids": anchor["stores"],
            "affected_weeks": anchor["weeks"],
            "avg_weekly_demand": dec["avg_weekly_demand"],
            "target_on_hand_units": dec["target_on_hand_units"],
            "orchestrator_request": scenario["orchestrator_request"],
            "stages": stages_out,
            "final_outcome": scenario["final_outcome"],
            "required_human_review": scenario["required_human_review"],
            "primary_reason": anchor["reason"],
            "top_policy_refs": anchor["refs"],
            "summary_explanation": anchor["summary"],
        }
        write_json(f"07_decision_ground_truth/{scenario['scenario_id']}.json", rollup, counter)
        csv_rows.append([
            scenario["scenario_id"], scenario["path"], anchor["sku"], ",".join(anchor["stores"]),
            anchor["type"], dec["approved_order_qty"], dec["anomaly_flag"], dec["expedite_required"],
            scenario["required_human_review"], scenario["final_outcome"],
        ])

    out = gt_dir / "ground_truth.csv"
    with open(out, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["scenario_id", "path", "sku_id", "store_ids", "scenario_type",
                    "approved_order_qty", "anomaly_flag", "expedite_required",
                    "required_human_review", "final_outcome"])
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
        scenario_type_counts[row[4]] = scenario_type_counts.get(row[4], 0) + 1

    summary = {
        "dataset_name": "retail-agentic-inventory-planning",
        "window_start": window_start,
        "window_end": window_end,
        "sku_count": 6,
        "store_count": 2,
        "category_count": 3,
        "document_counts": counts,
        "e2e_scenario_count": len(csv_rows),
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
    csv_rows = build_scenario_ground_truth(pos, inventory_rows, suppliers, shipments, promos, c)
    counts["decision_ground_truth"] = len(c)
    print(f"  -> {len(c)} files")

    build_dataset_summary(counts, csv_rows, dates[0], dates[-1])
    print(f"\nDone — normalized layers + ground truth written under {BASE}")


if __name__ == "__main__":
    main()
