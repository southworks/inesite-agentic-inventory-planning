# Agent Input Documents — PDF & PNG

Synthetic **PDF** and **PNG** files for AI document agents, rendered from `00_raw/`.
Complements the text/CSV Raw layer with formats agents commonly ingest in production
pipelines (OCR, vision, multi-format document pipelines) — mirrors the FSI
dataset-seed's `generate_agent_documents.py`, adapted from "per loan application" to
"per the natural reporting unit for each retail source system."

`00_agent_inputs/` is gitignored — it is a zero-new-information rendering of `00_raw/`,
regenerate after clone (see **Generate** below). Note: PDF byte content is deterministic
except for ReportLab's embedded `CreationDate`/`ModDate`/`ID` metadata, which changes on
every run — that's a ReportLab library default, not a data inconsistency.

## Category mapping

| Source folder (`00_raw/`) | Document | Granularity | PDF | PNG |
|---|---|---|---|---|
| `pos_transactions/` | Weekly Store Sales Report | per (store, week) — 22 | `pos_transactions/{store}_{week_start}_sales_report.pdf` | — |
| `inventory_snapshots/` | Weekly Inventory Status Report | per (store, week) — 22 | `inventory_snapshots/{store}_{date}_inventory_report.pdf` | — |
| `supplier_data/` (master) | Approved Supplier Profile | per supplier — 6 | `supplier_data/{supplier_id}_profile.pdf` | — |
| `supplier_data/` (shipments) | Shipment Receiving Report | per shipment — 6 | `supplier_data/{shipment_id}_receiving_report.pdf` | `supplier_data/{shipment_id}_packing_slip.png` |
| `promotions/` | Promotional Event Brief | per event — 4 | `promotions/{event_id}.pdf` | — |

**60 PDFs + 6 PNGs = 66 files.**

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

Re-run after editing `00_raw/` (or after re-running `generate_raw_layer.py`) to keep
these in sync.

## Relationship to other layers

```
00_raw/ (csv/txt system exports)
    ├── generate_normalized_layers.py → 01_pos_transactions/ ... 07_decision_ground_truth/
    └── generate_agent_documents.py   → 00_agent_inputs/{pdf,png}/...
```

Both scripts read `00_raw/` independently — `00_agent_inputs/` is an alternate
*format* of the same raw signals, not a dependency of the normalized JSON layers.

## Adding a new source file

1. Add the new raw file under `00_raw/<source_type>/` (and re-run `generate_raw_layer.py` if it's derived from the M5 extract).
2. Add a builder function in `generate_agent_documents.py` following the existing `generate_*` functions — use `write_pdf` for field/value documents (supplier profile, shipment report, promo brief) or `write_pdf_datatable` for multi-row reports (sales/inventory).
3. Add a PNG renderer only if the new source type has a believable physical-scan equivalent (see "Why only shipments get a PNG" above).
4. Update the **Category mapping** table above.
