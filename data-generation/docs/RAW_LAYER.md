# Raw Layer

The retail dataset starts from signal exports rather than borrower-style documents. Canonical source data is generated under `data-generation/corpus/`, then sliced into runtime cases under `dataset-seed/cases/`.

## Current layout

```text
data-generation/
  corpus/
    m5_extract.json
    pos_transactions/
    inventory_snapshots/
    promotions/
    supplier_data/
  scripts/
    scenarios.py
    generate_raw_layer.py
    generate_agent_documents.py
    build_case_folders.py
  ground-truth/

dataset-seed/
  cases/
    case-01/
      ingest/
      fabric-pre-requisite-data/
      README.md
    catalog.json
```

`data-generation/scripts/scenarios.py` is the single source of truth for the five e2e scenarios and maps legacy `IPF-XXX` ids to runtime `case-XX` folders.

## Regenerate

```bash
cd data-generation/scripts
python3 generate_raw_layer.py
pip install -r requirements.txt
python3 generate_agent_documents.py
python3 build_case_folders.py
```

`generate_agent_documents.py` is only required when optional PDF/PNG source renderings need to be refreshed. `build_case_folders.py` is the step that refreshes `dataset-seed/cases/`.
`generate_normalized_layers.py` refreshes validation answer keys only when the intermediate normalized catalog is present; it is not required for runtime case rebuilds.

## How to add a scenario

See [`../README.md`](../README.md#how-to-add-a-scenario).

## Source signal notes

The M5 extract provides coherent POS and price signals. Supplier, promotion, and inventory signals are synthesized to match the selected scenarios. When adding a signal, document whether it is sourced from M5 or injected by the generator.
