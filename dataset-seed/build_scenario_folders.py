#!/usr/bin/env python3
"""
Build the per-scenario Raw Layer folders for the retail inventory-planning dataset seed.

Only the agents that consume this data are materialized: Signal Ingestion and Forecasting.
The canonical exports in 00_raw/_full_exports/ plus the normalized layers 01_*..05_* and the
e2e rollups 07_decision_ground_truth/IPF-XXX.json are the single sources of truth.

    00_raw/IPF-XXX_<path>/
      01_signal_ingestion/
        agent_input.json
        input/                <- sliced raw exports (+ marquee pdf/png)
        expected_output/      <- normalized 01/02/03/04 entities + _expected_output.json
      02_forecasting/
        agent_input.json
        input/                <- scoped DMD-{sku}.json (05) + seasonal_planning_policy.txt (RAG)
        expected_output/      <- forecast_result.json + _expected_output.json
      scenario.json           <- e2e rollup mirror of 07_decision_ground_truth/IPF-XXX.json

Idempotent: existing 00_raw/IPF-*/ folders are removed and rebuilt. Offline (no fetch).
Run AFTER generate_normalized_layers.py.
"""

from __future__ import annotations

import csv
import json
import shutil
from pathlib import Path

from scenarios import SCENARIOS, scenario_folder, SCENARIO_FOLDER_STAGES, scenario_folder_stage

BASE = Path(__file__).resolve().parent
RAW = BASE / "00_raw"
CANON = RAW / "_full_exports"
GT_DIR = BASE / "07_decision_ground_truth"

POS_LAYER = BASE / "01_pos_transactions"
SUP_LAYER = BASE / "02_supplier_data"
PROMO_LAYER = BASE / "03_promotions"
INV_LAYER = BASE / "04_inventory"
DMD_LAYER = BASE / "05_demand_signals"
POLICY_LAYER = BASE / "06_policy_rag"

# RAG corpus for the Forecasting agent's "Short-term trend" action (SN-5xx refs).
FORECASTING_POLICY_FILES = ["seasonal_planning_policy.txt"]


# ─── Raw slicing (reads the canonical _full_exports/, scoped to one scenario) ────

def _read_csv(path: Path):
    with open(path, newline="", encoding="utf-8") as f:
        reader = csv.reader(f)
        return next(reader), list(reader)


def _emit_csv(out: Path, header: list, rows: list) -> None:
    out.parent.mkdir(parents=True, exist_ok=True)
    with open(out, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(header)
        w.writerows(rows)


def _emit_txt(out: Path, content: str) -> None:
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(content.strip() + "\n", encoding="utf-8")


def _slice_pos(skus, stores, dest: Path) -> int:
    n = 0
    for fp in sorted((CANON / "pos_transactions").glob("pos_export_*.csv")):
        header, rows = _read_csv(fp)
        kept = [r for r in rows if r[2] in skus and r[1] in stores]
        if kept:
            _emit_csv(dest / "pos_transactions" / fp.name, header, kept)
            n += 1
    return n


def _slice_inventory(skus, stores, dest: Path) -> int:
    header, rows = _read_csv(CANON / "inventory_snapshots" / "inventory_snapshot.csv")
    kept = [r for r in rows if r[2] in skus and r[1] in stores]
    if kept:
        _emit_csv(dest / "inventory_snapshots" / "inventory_snapshot.csv", header, kept)
        return 1
    return 0


def _slice_promotions(skus, stores, dest: Path) -> int:
    header, rows = _read_csv(CANON / "promotions" / "promo_calendar.csv")
    kept = [r for r in rows if r[1] in skus and r[2] in stores]
    if kept:
        _emit_csv(dest / "promotions" / "promo_calendar.csv", header, kept)
        return 1
    return 0


def _slice_supplier(suppliers, shipments, dest: Path) -> int:
    sep = "-" * 78
    n = 0
    for fname, ids in (("supplier_master.txt", suppliers), ("supplier_shipments.txt", shipments)):
        ids = set(ids)
        if not ids:
            continue
        text = (CANON / "supplier_data" / fname).read_text(encoding="utf-8")
        segments = text.split(sep)
        kept = [segments[0]] + [seg for seg in segments[1:] if any(i in seg for i in ids)]
        _emit_txt(dest / "supplier_data" / fname, sep.join(kept))
        n += 1
    return n


def slice_raw(raw_slice: dict, dest: Path) -> int:
    skus, stores = set(raw_slice["skus"]), set(raw_slice["stores"])
    slicers = {
        "pos_transactions": lambda: _slice_pos(skus, stores, dest),
        "inventory_snapshots": lambda: _slice_inventory(skus, stores, dest),
        "promotions": lambda: _slice_promotions(skus, stores, dest),
        "supplier_data": lambda: _slice_supplier(raw_slice.get("suppliers", []),
                                                 raw_slice.get("shipments", []), dest),
    }
    return sum(slicers[s]() for s in raw_slice["sources"])


def _copy_file(src: Path, dest: Path) -> int:
    if src.is_file():
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dest)
        return 1
    return 0


def copy_marquee_documents(raw_slice: dict, inp: Path) -> int:
    n = 0
    sup_src, sup_dst = CANON / "supplier_data", inp / "supplier_data"
    for shp in raw_slice.get("shipments", []):
        n += _copy_file(sup_src / f"{shp}_receiving_report.pdf", sup_dst / f"{shp}_receiving_report.pdf")
        n += _copy_file(sup_src / f"{shp}_packing_slip.png", sup_dst / f"{shp}_packing_slip.png")
    for sup in raw_slice.get("suppliers", []):
        n += _copy_file(sup_src / f"{sup}_profile.pdf", sup_dst / f"{sup}_profile.pdf")
    for event in raw_slice.get("promo_events", []):
        n += _copy_file(CANON / "promotions" / f"{event}.pdf", inp / "promotions" / f"{event}.pdf")
    return n


def _copy_globs(layer: Path, patterns: list, dest: Path) -> int:
    dest.mkdir(parents=True, exist_ok=True)
    n = 0
    for pat in patterns:
        for fp in sorted(layer.glob(pat)):
            shutil.copy2(fp, dest / fp.name)
            n += 1
    return n


def _copy_named(layer: Path, names: list, dest: Path) -> int:
    dest.mkdir(parents=True, exist_ok=True)
    n = 0
    for name in names:
        fp = layer / name
        if fp.is_file():
            shutil.copy2(fp, dest / fp.name)
            n += 1
    return n


def _write_scoped_demand_signal(sku: str, stores: list, dest: Path) -> int:
    src = DMD_LAYER / f"DMD-{sku}.json"
    if not src.is_file():
        return 0
    data = json.loads(src.read_text(encoding="utf-8"))
    data["stores"] = {store: data["stores"][store] for store in stores if store in data["stores"]}
    dest.mkdir(parents=True, exist_ok=True)
    (dest / src.name).write_text(json.dumps(data, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return 1


def stage_inputs(stage_name: str, scenario: dict, scenario_dir: Path) -> int:
    rs = scenario["raw_slice"]
    skus, stores = rs["skus"], rs["stores"]
    inp = scenario_dir / scenario_folder_stage(stage_name) / "input"
    files = 0

    if stage_name == "signal_ingestion":
        files += slice_raw(rs, inp)
        files += copy_marquee_documents(rs, inp)
    elif stage_name == "forecasting":
        files += _copy_named(POLICY_LAYER, FORECASTING_POLICY_FILES, inp)
        for sku in skus:
            files += _write_scoped_demand_signal(sku, stores, inp)
    return files


def stage_outputs(stage_name: str, scenario: dict, stage_dir: Path) -> int:
    rs = scenario["raw_slice"]
    skus, stores = rs["skus"], rs["stores"]
    out = stage_dir / "expected_output"
    files = 0

    if stage_name == "signal_ingestion":
        for sku in skus:
            for store in stores:
                files += _copy_globs(POS_LAYER, [f"POS-{sku}-{store}-*.json"], out)
                files += _copy_globs(INV_LAYER, [f"INV-{sku}-{store}-*.json"], out)
        files += _copy_globs(SUP_LAYER, [f"{s}.json" for s in rs.get("suppliers", [])], out)
        files += _copy_globs(PROMO_LAYER, [f"{e}.json" for e in rs.get("promo_events", [])], out)
    out.mkdir(parents=True, exist_ok=True)
    return files


def write_forecast_artifact(rstage: dict, out_dir: Path) -> int:
    out_dir.mkdir(parents=True, exist_ok=True)
    payload = {
        "stage": "forecasting",
        "agent": rstage["agent"],
        "decision": rstage["decision"],
        "gate": rstage["gate"],
        "policy_refs": rstage["policy_refs"],
        "artifact_type": "forecast_result",
        "forecast_result": rstage["expected_output"],
    }
    (out_dir / "forecast_result.json").write_text(
        json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return 1


def main() -> None:
    for child in RAW.iterdir():
        if child.is_dir() and child.name != "_full_exports":
            shutil.rmtree(child)

    total_files = 0
    for scenario in SCENARIOS:
        sid = scenario["scenario_id"]
        rollup = json.loads((GT_DIR / f"{sid}.json").read_text(encoding="utf-8"))
        rollup_stages = {s["stage"]: s for s in rollup["stages"]}
        scenario_dir = RAW / scenario_folder(scenario)
        files = 0

        for stage_name in SCENARIO_FOLDER_STAGES:
            rstage = rollup_stages[stage_name]
            stage_dir = scenario_dir / scenario_folder_stage(stage_name)
            stage_dir.mkdir(parents=True, exist_ok=True)

            (stage_dir / "agent_input.json").write_text(
                json.dumps(rstage["agent_input"], indent=2, sort_keys=True) + "\n",
                encoding="utf-8")

            files += stage_inputs(stage_name, scenario, scenario_dir)
            files += stage_outputs(stage_name, scenario, stage_dir)

            expected_dir = stage_dir / "expected_output"
            (expected_dir / "_expected_output.json").write_text(
                json.dumps({
                    "stage": stage_name,
                    "agent": rstage["agent"],
                    "decision": rstage["decision"],
                    "gate": rstage["gate"],
                    "policy_refs": rstage["policy_refs"],
                    "expected_output": rstage["expected_output"],
                }, indent=2, sort_keys=True) + "\n",
                encoding="utf-8")
            files += 2
            if stage_name == "forecasting":
                files += write_forecast_artifact(rstage, expected_dir)

        shutil.copy2(GT_DIR / f"{sid}.json", scenario_dir / "scenario.json")
        files += 1

        total_files += files
        print(f"{scenario_folder(scenario)}: {files} files across {len(SCENARIO_FOLDER_STAGES)} stages")

    print(f"\nDone — {total_files} files written into per-scenario folders "
          f"under {RAW.relative_to(BASE.parent)}/ ({len(SCENARIOS)} scenarios)")


if __name__ == "__main__":
    main()
