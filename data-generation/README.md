# Retail Data Generation

Reference material for building and validating the retail inventory-planning dataset. Not required to run a demo — use [`../dataset-seed/`](../dataset-seed/) for that.

## Layout

```
data-generation/
  corpus/                    m5_extract.json + canonical csv/txt exports (POS, supplier, promo, inventory)
  ground-truth/              optional e2e answer keys (IPF-001 … IPF-005) for validation
  scripts/                   generators and build_case_folders.py
  docs/                      handoff, testing guide, schemas
```

## Regenerate demo cases

```bash
cd data-generation/scripts
python3 generate_raw_layer.py       # corpus/ csv|txt exports (only when corpus signals change)
python3 build_case_folders.py       # dataset-seed/cases/*/ingest/ + fabric-pre-requisite-data/
python3 build_case_folders.py --scenario IPF-XXX   # single scenario only
```

Optional:

```bash
pip install -r requirements.txt
python3 generate_agent_documents.py        # pdf/png into corpus/
```

`generate_normalized_layers.py` refreshes validation answer keys only when the intermediate normalized catalog is present; it is not required to rebuild runtime demo cases.

Demo data lands directly in `dataset-seed/cases/`.

## How to add a scenario

New scenarios are generated into `dataset-seed/` and only affect the running app after the generated assets are rebuilt, container images or deployment packages are republished, and Azure is redeployed.

1. Add or update the source signal in `corpus/` via `scripts/generate_raw_layer.py`.
2. Add the `IPF-XXX` scenario and `IPF-XXX` -> `case-XX` mapping in `scripts/scenarios.py`.
3. Run the generation commands above.
4. Review `../dataset-seed/cases/{caseId}/`, `../dataset-seed/cases/catalog.json`, and `ground-truth/`.
5. Add the new `case-XX` to `SupportedCaseIds` in `../backend/GrokInventoryAndTrend.Api/Services/LocalDocumentStorageService.cs`.
6. Rebuild and redeploy the app assets that embed `dataset-seed/`.

## Key docs

- [`docs/HANDOFF.md`](docs/HANDOFF.md) — five-agent handoff map
- [`docs/TESTING_GUIDE.md`](docs/TESTING_GUIDE.md) — Fabric upload + e2e demo runbook
- [`docs/TEST_CASES.md`](docs/TEST_CASES.md) — scenario index
- [`docs/TEAM_HANDOFF.md`](docs/TEAM_HANDOFF.md) — structural change summary for integration teams
