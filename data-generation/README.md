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
python3 generate_raw_layer.py       # corpus/ csv|txt exports
python3 build_case_folders.py       # dataset-seed/cases/*/ingest/ + fabric-pre-requisite-data/
```

Optional:

```bash
pip install -r requirements.txt
python3 generate_agent_documents.py        # pdf/png into corpus/
python3 generate_normalized_layers.py      # ground-truth/ only (validation answer keys)
```

No `entity-catalog/`, `expected-outputs/`, or `source-exports/` are produced — demo data lands directly in `dataset-seed/cases/`.

## Key docs

- [`docs/HANDOFF.md`](docs/HANDOFF.md) — five-agent handoff map
- [`docs/TESTING_GUIDE.md`](docs/TESTING_GUIDE.md) — Fabric upload + e2e demo runbook
- [`docs/TEST_CASES.md`](docs/TEST_CASES.md) — scenario index
- [`docs/TEAM_HANDOFF.md`](docs/TEAM_HANDOFF.md) — structural change summary for integration teams
