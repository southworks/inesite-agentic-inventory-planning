# Testing Guide

Run demos using the committed runtime package under `dataset-seed/cases/`.

## Quick start

1. Pick a case id from `dataset-seed/cases/catalog.json` (`case-01` through `case-05`).
2. Start the workflow with `POST /api/inventory-planning/cases/{caseId}/workflow/basic/start`.
3. Poll `GET /api/inventory-planning/executions/{executionId}/basic/status`.
4. Compare the final output with `data-generation/ground-truth/IPF-XXX.json` when validation is needed.

The deployed MCP reads case-scoped data from the bundled `dataset-seed/cases/{caseId}/fabric-pre-requisite-data/` inside the MCP container. There is no runtime endpoint for creating cases or uploading new signal packages.

## Rebuild test data

```bash
cd data-generation/scripts
python3 generate_raw_layer.py
python3 build_case_folders.py
```

Run `pip install -r requirements.txt` and `python3 generate_agent_documents.py` first if source PDFs or PNGs need to be refreshed.
Run `generate_normalized_layers.py` only when refreshing validation answer keys and the intermediate normalized catalog is present.
