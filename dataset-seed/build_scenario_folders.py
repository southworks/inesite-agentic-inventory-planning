#!/usr/bin/env python3
"""
Build the per-scenario, per-agent Raw Layer folders for the retail inventory-planning
dataset seed.

The test cases are end-to-end: each scenario is one full path through the workflow
(Orchestrator -> Signal Ingestion -> Feature & Causality -> Forecasting ->
Replenishment & Allocation -> Planner Copilot), differing at the human-in-the-loop
gates. The scenario set is defined once in `scenarios.py` (imported here and by
generate_normalized_layers.py).

The canonical exports in 00_raw/_full_exports/ (produced by generate_raw_layer.py) plus
the normalized layers 01_*..05_* and the e2e rollups 07_decision_ground_truth/IPF-XXX.json
(produced by generate_normalized_layers.py) are the single sources of truth. This script
creates, per scenario, one folder with a sub-folder per stage:

    00_raw/IPF-XXX_<path>/
      01_orchestrator/        request.json
      02_signal_ingestion/
        agent_input.json      <- structured payload to START this agent in isolation
        input/                <- raw exports the agent ingests (sliced from _full_exports/)
        expected_output/      <- the normalized entities it would produce, so the next agent
          _expected_output.json  can start without running this one
      03_feature_causality/   (same shape; input = upstream entities)
      04_forecasting/
        expected_output/forecast_result.json
      05_replenishment_allocation/
        input/forecast_result.json
        expected_output/replenishment_plan.json
      06_planner_copilot/
        input/replenishment_plan.json
        expected_output/planner_decision.json
      scenario.json           <- e2e rollup mirror of 07_decision_ground_truth/IPF-XXX.json

Because the workflow is signal-based, raw files and normalized entities are intentionally
duplicated into the stage/scenario folders; the canonical copies in _full_exports/ and the
01_*..05_* layers remain the source the pipeline (generate_normalized_layers.py) reads.

Idempotent: existing 00_raw/IPF-*/ folders are removed and rebuilt. Offline (no fetch).
Run AFTER generate_normalized_layers.py.
"""

from __future__ import annotations

import csv
import json
import shutil
from pathlib import Path

from scenarios import SCENARIOS, scenario_folder, STAGE_FOLDERS

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

PLANNER_POLICY_FILES = ["service_level_policy.txt", "budget_allocation_policy.txt",
                        "replenishment_policy.txt"]

STAGE_ARTIFACTS = {
    "forecasting": ("forecast_result.json", "forecast_result"),
    "replenishment_allocation": ("replenishment_plan.json", "replenishment_plan"),
    "planner_copilot": ("planner_decision.json", "planner_decision"),
}

UPSTREAM_ARTIFACTS = {
    "replenishment_allocation": [("forecasting", "forecast_result.json")],
    "planner_copilot": [("replenishment_allocation", "replenishment_plan.json")],
}


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
        header, rows = _read_csv(fp)  # TRANS_DATE,STORE_ID,SKU,...
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
    header, rows = _read_csv(CANON / "promotions" / "promo_calendar.csv")  # EVENT_ID,SKU,STORE_ID,...
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
    """Best-effort copy (skips silently if the rendering wasn't generated)."""
    if src.is_file():
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dest)
        return 1
    return 0


def copy_marquee_documents(raw_slice: dict, inp: Path) -> int:
    """Copy the marquee per-entity pdf/png renderings (shipment receiving report + packing
    slip, supplier profile, promo brief) co-located in _full_exports/<source_type>/ into the
    Signal-Ingestion input/ so each case is a self-contained OCR/vision demo. Best-effort:
    no-op when generate_agent_documents.py hasn't been run yet."""
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


# ─── Entity copying ─────────────────────────────────────────────────────────────

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


# ─── Per-stage input/ + expected_output/ entity selection ───────────────────────

def copy_upstream_artifacts(stage_name: str, scenario_dir: Path, dest: Path) -> int:
    """Copy persisted outputs from prior stages into this stage's input/ folder.

    These files materialize the memory/context handoff described in workflow-summary.md:
    Forecasting hands a forecast to Replenishment, then Replenishment hands a proposed
    plan/order to Planner Copilot.
    """
    n = 0
    for upstream_stage, artifact_name in UPSTREAM_ARTIFACTS.get(stage_name, []):
        src = scenario_dir / STAGE_FOLDERS[upstream_stage] / "expected_output" / artifact_name
        n += _copy_file(src, dest / artifact_name)
    return n


def stage_inputs(stage_name: str, scenario: dict, stage_dir: Path, scenario_dir: Path) -> int:
    """Materialize the input/ folder: the documents that START this agent in isolation."""
    rs = scenario["raw_slice"]
    anchor = scenario["anchor"]
    skus, stores = rs["skus"], rs["stores"]
    inp = stage_dir / "input"
    files = copy_upstream_artifacts(stage_name, scenario_dir, inp)

    if stage_name == "signal_ingestion":
        files += slice_raw(rs, inp)  # raw exports it ingests
        files += copy_marquee_documents(rs, inp)  # rendered pdf/png the OCR/vision agent reads
    elif stage_name == "feature_causality":
        for sku in skus:
            for store in stores:
                files += _copy_globs(POS_LAYER, [f"POS-{sku}-{store}-*.json"], inp)
        files += _copy_globs(PROMO_LAYER, [f"{e}.json" for e in rs.get("promo_events", [])], inp)
    elif stage_name == "forecasting":
        files += _copy_globs(DMD_LAYER, [f"DMD-{sku}.json" for sku in skus], inp)
    elif stage_name == "replenishment_allocation":
        for sku in skus:
            for store in stores:
                pats = [f"INV-{sku}-{store}-{wk}.json" for wk in anchor["weeks"]]
                files += _copy_globs(INV_LAYER, pats, inp)
        files += _copy_globs(SUP_LAYER, [f"{s}.json" for s in rs.get("suppliers", [])], inp)
    elif stage_name == "planner_copilot":
        files += _copy_named(POLICY_LAYER, PLANNER_POLICY_FILES, inp)
    return files


def stage_outputs(stage_name: str, scenario: dict, stage_dir: Path) -> int:
    """Materialize expected_output/ entity files.

    Forecasting/Replenishment/Planner also write compact JSON artifacts in main(),
    because those stage outputs become the next agent's input contract.
    """
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
    elif stage_name == "feature_causality":
        files += _copy_globs(DMD_LAYER, [f"DMD-{sku}.json" for sku in skus], out)
    out.mkdir(parents=True, exist_ok=True)
    return files


def write_stage_artifact(stage_name: str, rstage: dict, out_dir: Path) -> int:
    artifact = STAGE_ARTIFACTS.get(stage_name)
    if not artifact:
        return 0
    filename, payload_key = artifact
    out_dir.mkdir(parents=True, exist_ok=True)
    payload = {
        "stage": stage_name,
        "agent": rstage["agent"],
        "decision": rstage["decision"],
        "gate": rstage["gate"],
        "policy_refs": rstage["policy_refs"],
        "artifact_type": payload_key,
        payload_key: rstage["expected_output"],
    }
    (out_dir / filename).write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return 1


# ─── Build ──────────────────────────────────────────────────────────────────────

def main() -> None:
    # Clear existing scenario folders (everything in 00_raw/ except _full_exports).
    for child in RAW.iterdir():
        if child.is_dir() and child.name != "_full_exports":
            shutil.rmtree(child)

    total_files = 0
    for scenario in SCENARIOS:
        sid = scenario["scenario_id"]
        rollup = json.loads((GT_DIR / f"{sid}.json").read_text(encoding="utf-8"))
        rollup_stages = {s["stage"]: s for s in rollup["stages"]}
        scenario_dir = RAW / scenario_folder(scenario)

        # 01_orchestrator/request.json
        orch_dir = scenario_dir / STAGE_FOLDERS["orchestrator"]
        orch_dir.mkdir(parents=True, exist_ok=True)
        (orch_dir / "request.json").write_text(
            json.dumps(scenario["orchestrator_request"], indent=2, sort_keys=True) + "\n",
            encoding="utf-8")

        files = 1
        for stage in scenario["stages"]:
            name = stage["stage"]
            rstage = rollup_stages[name]
            stage_dir = scenario_dir / STAGE_FOLDERS[name]
            stage_dir.mkdir(parents=True, exist_ok=True)

            # agent_input.json — the payload to start this agent in isolation.
            (stage_dir / "agent_input.json").write_text(
                json.dumps(rstage["agent_input"], indent=2, sort_keys=True) + "\n", encoding="utf-8")

            files += stage_inputs(name, scenario, stage_dir, scenario_dir)
            files += stage_outputs(name, scenario, stage_dir)

            # expected_output/_expected_output.json — the decision + measurable expectations.
            expected_dir = stage_dir / "expected_output"
            (expected_dir / "_expected_output.json").write_text(
                json.dumps({
                    "stage": name, "agent": rstage["agent"], "decision": rstage["decision"],
                    "gate": rstage["gate"], "policy_refs": rstage["policy_refs"],
                    "expected_output": rstage["expected_output"],
                }, indent=2, sort_keys=True) + "\n", encoding="utf-8")
            files += 2  # agent_input.json + _expected_output.json
            files += write_stage_artifact(name, rstage, expected_dir)

        # scenario.json — self-contained copy of the e2e rollup.
        shutil.copy2(GT_DIR / f"{sid}.json", scenario_dir / "scenario.json")
        files += 1

        total_files += files
        print(f"{scenario_folder(scenario)}: {files} files across {len(scenario['stages'])} stages")

    print(f"\nDone — {total_files} files written into per-scenario / per-agent folders "
          f"under {RAW.relative_to(BASE.parent)}/ ({len(SCENARIOS)} scenarios)")


if __name__ == "__main__":
    main()
