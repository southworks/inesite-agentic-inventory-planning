# Agent Input Documents

Generated source files live under `data-generation/corpus/` and are copied or sliced into the runtime package by `data-generation/scripts/build_case_folders.py`.

## Runtime layout

```text
dataset-seed/cases/{caseId}/
  ingest/                       flat POS, inventory, supplier, and promotion files
  fabric-pre-requisite-data/    normalized JSON grouped by MCP document type
```

The API exposes files from `ingest/`. MCP tools read the normalized case data from `fabric-pre-requisite-data/` in the bundled dataset package.

## Source categories

| Source type | Runtime role |
| --- | --- |
| `pos_transactions/` | store sales signal |
| `inventory_snapshots/` | on-hand, in-transit, safety stock signal |
| `supplier_data/` | supplier profile, shipment delay, and fill-rate signal |
| `promotions/` | promotion and uplift signal |

## Regenerate

```bash
cd data-generation/scripts
python3 generate_raw_layer.py
pip install -r requirements.txt
python3 generate_agent_documents.py
python3 build_case_folders.py
```

Run `generate_agent_documents.py` only when the optional PDF/PNG source renderings need to be refreshed.
