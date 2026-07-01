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

New scenarios are not injected into a running app. They become available only after regenerating `dataset-seed/`, rebuilding images or deployment packages, and redeploying.

1. Add or update the source signal in `data-generation/corpus/` by editing the constants or extract inputs used by `generate_raw_layer.py`.
2. Add an `IPF-XXX` entry to `SCENARIOS` in `data-generation/scripts/scenarios.py`.
3. Add the `IPF-XXX` -> `case-XX` entry to `CASE_FOLDERS` in the same file.
4. Regenerate the dataset with the commands above.
5. Review `dataset-seed/cases/{caseId}/`, `dataset-seed/cases/catalog.json`, and `data-generation/ground-truth/`.
6. Add the new runtime case id to `SupportedCaseIds` in `backend/GrokInventoryAndTrend.Api/Services/LocalDocumentStorageService.cs`; otherwise the API rejects it even if the folder exists.
7. Update scenario docs and rebuild/redeploy the app assets that embed `dataset-seed/`.

## Source signal notes

The M5 extract provides coherent POS and price signals. Supplier, promotion, and inventory signals are synthesized to match the selected scenarios. When adding a signal, document whether it is sourced from M5 or injected by the generator.
