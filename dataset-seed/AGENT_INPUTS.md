# Agent Input Documents — PDF & PNG

Synthetic **PDF** and **PNG** files for AI document agents, rendered from the canonical
csv/txt exports and **co-located by source type** under `00_raw/_full_exports/<source_type>/`.
Complements the text/CSV Raw layer with formats agents commonly ingest in production
pipelines (OCR, vision, multi-format document pipelines). The marquee per-entity documents
(shipment receiving report + packing slip, supplier profile, promo brief) are also **copied by
`build_scenario_folders.py` into each scenario's Signal-Ingestion input**
(`00_raw/IPF-XXX_<path>/01_signal_ingestion/input/<source_type>/`) so each test case is a
self-contained OCR/vision demo — see [HANDOFF.md](HANDOFF.md), [RAW_LAYER.md](RAW_LAYER.md), and
[TEST_CASES.md](TEST_CASES.md).

`00_raw/` is committed — all dataset-seed data ships in the repo as-is, no script run
required to use it. `generate_agent_documents.py` (see **Generate** below) is only needed
to *regenerate* these files after the csv/txt raw files change.
Note: PDF byte content is deterministic except for ReportLab's embedded
`CreationDate`/`ModDate`/`ID` metadata, which changes on every run — that's a ReportLab
library default, not a data inconsistency, so a re-run will show as a diff on every PDF
even with no real change.

## Category mapping

Paths are relative to `00_raw/_full_exports/<source_type>/` (the canonical store).

| Source type | Document | Granularity | PDF | PNG |
|---|---|---|---|---|
| `pos_transactions/` | Weekly Store Sales Report | per (store, week) — 22 | `{store}_{week_start}_sales_report.pdf` | — |
| `inventory_snapshots/` | Weekly Inventory Status Report | per (store, week) — 22 | `{store}_{date}_inventory_report.pdf` | — |
| `supplier_data/` (master) | Approved Supplier Profile | per supplier — 6 | `{supplier_id}_profile.pdf` | — |
| `supplier_data/` (shipments) | Shipment Receiving Report | per shipment — 6 | `{shipment_id}_receiving_report.pdf` | `{shipment_id}_packing_slip.png` |
| `promotions/` | Promotional Event Brief | per event — 4 | `{event_id}.pdf` | — |

**60 PDFs + 6 PNGs = 66 files** under `00_raw/_full_exports/<source_type>/`. The shipment,
supplier-profile and promo-brief documents that map to a scenario are additionally copied by
`build_scenario_folders.py` into that scenario's `01_signal_ingestion/input/<source_type>/`. The
bulky multi-SKU store-week POS/inventory report PDFs stay only in `_full_exports/`; the
per-scenario csv slices already carry that signal.

## Why only shipments get a PNG

FSI's PNGs simulate a borrower-submitted physical scan (driver's license, paystub).
Retail's raw signals are system exports, not borrower scans — there's no realistic
"scan" of a CSV. The one believable exception is the **delivery dock**: a warehouse
receiving clerk physically scans a packing slip/delivery receipt when a shipment
arrives. That's the only category here with a true scan equivalent, so it's the only
one rendered as both a PDF (the digital ERP record) and a PNG (the dock scan) — same
dual-format treatment FSI gives paystubs.

## Generate

```bash
cd dataset-seed
pip install -r requirements.txt
python3 generate_agent_documents.py              # all categories, pdf + png
python3 generate_agent_documents.py --formats pdf
python3 generate_agent_documents.py --formats png
```

Re-run after editing the canonical `00_raw/_full_exports/<source_type>/*.csv|*.txt` (or
after re-running `generate_raw_layer.py`) to keep these in sync. Then re-run
`build_scenario_folders.py` to refresh the per-scenario copies.

## Relationship to other layers

```
00_raw/
├── _full_exports/<source_type>/        ← canonical csv/txt exports + pdf/png renderings
└── IPF-XXX_<path>/
    ├── 01_signal_ingestion/input/<source_type>/   ← sliced csv/txt + copied marquee pdf/png
    ├── 02_forecasting/input/                      ← scoped DMD + seasonal_planning_policy.txt
    └── scenario.json                              ← full e2e answer key (all five agents)

00_raw/_full_exports/ → generate_normalized_layers.py → 01_pos_transactions/ ... 07_decision_ground_truth/
                      → build_scenario_folders.py     → 00_raw/IPF-XXX_<path>/ (two MCP stages)
```

`generate_normalized_layers.py` reads only the canonical csv/txt under `00_raw/_full_exports/`;
`build_scenario_folders.py` then copies the renderings + normalized entities into the scenario
folders. The per-scenario folders and the pdf/png renderings are alternate *views/formats* of the
same raw signals, not a dependency of the normalized JSON layers.

## Adding a new source file

1. Add the new raw file under `00_raw/_full_exports/<source_type>/` (and re-run `generate_raw_layer.py` if it's derived from the M5 extract).
2. Add a builder function in `generate_agent_documents.py` following the existing `generate_*` functions — use `write_pdf` for field/value documents (supplier profile, shipment report, promo brief) or `write_pdf_datatable` for multi-row reports (sales/inventory).
3. Add a PNG renderer only if the new source type has a believable physical-scan equivalent (see "Why only shipments get a PNG" above).
4. Update the **Category mapping** table above.
