#!/usr/bin/env python3
"""
Regenerate dataset-seed demo ingest/ folders from data-generation expected-outputs.

Maps Case folders to legacy IPF scenario folders, then copies only the Fabric upload
payload from each MCP stage (signal_ingestion + forecasting).
"""

from __future__ import annotations

import shutil
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
DATA_GEN = SCRIPTS.parent
REPO = DATA_GEN.parent
EXPECTED = DATA_GEN / "expected-outputs"
DATASET_SEED = REPO / "dataset-seed"

# demo folder -> legacy scenario folder under expected-outputs
INGEST_SOURCES: dict[str, tuple[str, str, str]] = {
    "cases/case-01-seasonal-happy-path": (
        "IPF-001_seasonal_happy_path",
        "01_signal_ingestion",
        "02_forecasting",
    ),
    "cases/case-02-promotion-budget-review": (
        "IPF-002_promotion_spike_budget_review",
        "01_signal_ingestion",
        "02_forecasting",
    ),
    "cases/case-03-supplier-delay-expedite": (
        "IPF-003_supplier_delay_stockout_expedite",
        "01_signal_ingestion",
        "02_forecasting",
    ),
    "cases/case-04-partial-fill-reorder": (
        "IPF-004_partial_fill_stockout_reorder",
        "01_signal_ingestion",
        "02_forecasting",
    ),
    "cases/case-05-demand-anomaly": (
        "IPF-005_demand_anomaly_no_action",
        "01_signal_ingestion",
        "02_forecasting",
    ),
}

STAGE_DST = {
    "01_signal_ingestion": "signal_ingestion",
    "02_forecasting": "forecasting",
}


def sync_stage(demo_rel: str, scenario_name: str, stage: str) -> int:
    src = EXPECTED / scenario_name / stage / "input"
    dst = DATASET_SEED / demo_rel / "ingest" / STAGE_DST[stage]
    if not src.is_dir():
        raise FileNotFoundError(f"missing ingest source: {src}")
    if dst.exists():
        shutil.rmtree(dst)
    shutil.copytree(src, dst)
    return sum(1 for _ in dst.rglob("*") if _.is_file())


def main() -> None:
    total = 0
    for demo_rel, (scenario_name, sig_stage, fc_stage) in INGEST_SOURCES.items():
        count = sync_stage(demo_rel, scenario_name, sig_stage)
        count += sync_stage(demo_rel, scenario_name, fc_stage)
        print(f"{demo_rel}: {count} files")
        total += count
    print(f"\nDone — {total} ingest files synced to dataset-seed/")


if __name__ == "__main__":
    main()
