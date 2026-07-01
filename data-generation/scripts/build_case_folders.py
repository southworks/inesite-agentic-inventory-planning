#!/usr/bin/env python3
"""
Build dataset-seed/cases/ demo folders from corpus exports.

Each case folder matches the committed demo layout:

    dataset-seed/cases/case-XX_<path>/
      README.md                     (preserved — not overwritten)
      ingest/                       flat Fabric upload payload
        pos_export_*.csv
        inventory_snapshot.csv
        promo_calendar.csv          (when applicable)
        supplier_master.txt         (when applicable)
        supplier_shipments.txt      (when applicable)
      fabric-pre-requisite-data/    normalized entity JSON grouped by document_type
        inventory_snapshot/, pos_transaction_batch/, supplier_profile/, promotion_event/

Run AFTER generate_raw_layer.py.
"""

from __future__ import annotations

import argparse
import csv
import shutil
from pathlib import Path

from generate_normalized_layers import load_corpus, write_scenario_prerequisites
from scenarios import SCENARIOS, case_folder

SCRIPTS = Path(__file__).resolve().parent
DATA_GEN = SCRIPTS.parent
REPO = DATA_GEN.parent
CANON = DATA_GEN / "corpus"
CASES_DIR = REPO / "dataset-seed" / "cases"


def _read_csv(path: Path):
    with open(path, newline="", encoding="utf-8") as f:
        reader = csv.reader(f)
        return next(reader), list(reader)


def _emit_csv(out: Path, header: list, rows: list) -> None:
    out.parent.mkdir(parents=True, exist_ok=True)
    with open(out, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f, lineterminator="\n")
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
            _emit_csv(dest / fp.name, header, kept)
            n += 1
    return n


def _slice_inventory(skus, stores, dest: Path) -> int:
    header, rows = _read_csv(CANON / "inventory_snapshots" / "inventory_snapshot.csv")
    kept = [r for r in rows if r[2] in skus and r[1] in stores]
    if kept:
        _emit_csv(dest / "inventory_snapshot.csv", header, kept)
        return 1
    return 0


def _slice_promotions(skus, stores, dest: Path) -> int:
    header, rows = _read_csv(CANON / "promotions" / "promo_calendar.csv")
    kept = [r for r in rows if r[1] in skus and r[2] in stores]
    if kept:
        _emit_csv(dest / "promo_calendar.csv", header, kept)
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
        _emit_txt(dest / fname, sep.join(kept))
        n += 1
    return n


def slice_ingest(raw_slice: dict, dest: Path) -> int:
    skus, stores = set(raw_slice["skus"]), set(raw_slice["stores"])
    slicers = {
        "pos_transactions": lambda: _slice_pos(skus, stores, dest),
        "inventory_snapshots": lambda: _slice_inventory(skus, stores, dest),
        "promotions": lambda: _slice_promotions(skus, stores, dest),
        "supplier_data": lambda: _slice_supplier(raw_slice.get("suppliers", []),
                                                 raw_slice.get("shipments", []), dest),
    }
    return sum(slicers[s]() for s in raw_slice["sources"])


def build_case(scenario: dict, corpus: dict) -> tuple[int, int]:
    case_dir = CASES_DIR / case_folder(scenario)
    ingest_dir = case_dir / "ingest"
    prereq_dir = case_dir / "fabric-pre-requisite-data"

    if ingest_dir.exists():
        shutil.rmtree(ingest_dir)
    ingest_dir.mkdir(parents=True)
    ingest_files = slice_ingest(scenario["raw_slice"], ingest_dir)
    prereq_files = write_scenario_prerequisites(scenario, prereq_dir, corpus)
    return ingest_files, prereq_files


def main() -> None:
    parser = argparse.ArgumentParser(description="Build dataset-seed/cases/ from corpus exports.")
    parser.add_argument(
        "--scenario",
        metavar="IPF-XXX",
        help="Build only this scenario id (default: all scenarios in scenarios.py).",
    )
    args = parser.parse_args()

    scenarios = SCENARIOS
    if args.scenario:
        scenarios = [s for s in SCENARIOS if s["scenario_id"] == args.scenario]
        if not scenarios:
            raise SystemExit(f"Unknown scenario id: {args.scenario}")

    corpus = load_corpus()
    ingest_total = 0
    prereq_total = 0
    for scenario in scenarios:
        ingest_files, prereq_files = build_case(scenario, corpus)
        rel = f"cases/{case_folder(scenario)}"
        print(f"{rel}: {ingest_files} ingest + {prereq_files} prerequisite files")
        ingest_total += ingest_files
        prereq_total += prereq_files
    print(
        f"\nDone — {ingest_total} ingest files and {prereq_total} prerequisite files "
        f"written under dataset-seed/cases/"
    )


if __name__ == "__main__":
    main()
