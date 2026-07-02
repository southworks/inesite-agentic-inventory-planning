# Team Handoff

The current demo package is `dataset-seed/cases/`. The runtime contract is case-folder based and is produced by `build_case_folders.py`.

## Runtime contract

```text
dataset-seed/cases/
  catalog.json
  case-01/
    ingest/
    fabric-pre-requisite-data/
```

The API and UI use `case-01` through `case-05`. If a new case is added, update `SupportedCaseIds` in `backend/GrokInventoryAndTrend.Api/Services/LocalDocumentStorageService.cs` in addition to regenerating the dataset.

## Rebuild

```bash
cd data-generation/scripts
python3 generate_raw_layer.py
python3 build_case_folders.py
```

Run `generate_normalized_layers.py` only when refreshing validation answer keys and the intermediate normalized catalog is present.
Rebuild and redeploy any container image or deployment package that embeds `dataset-seed/`.
