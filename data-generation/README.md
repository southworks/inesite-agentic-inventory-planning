# Retail Data Generation

Reference material for building and validating the retail inventory-planning dataset. Not required to run a demo — use [`../dataset-seed/`](../dataset-seed/) for that.

## Layout

```
data-generation/
  source/                    m5_extract.json (curated M5 competition extract)
  source-exports/_full_exports/   canonical csv/txt + pdf/png system exports
  entity-catalog/            normalized JSON layers 01–06 (POS, supplier, promo, inventory, demand, policies)
  ground-truth/07_decision_ground_truth/   e2e answer keys (IPF-001 … IPF-005)
  expected-outputs/          per-scenario MCP folders (signal_ingestion + forecasting stages)
  scripts/                   generators and sync_demo_ingest.py
  docs/                      handoff, testing guide, schemas
```

## Regenerate

```bash
cd data-generation/scripts
python3 generate_raw_layer.py              # source-exports/_full_exports/
pip install -r requirements.txt
python3 generate_agent_documents.py        # pdf/png into _full_exports/
python3 generate_normalized_layers.py      # entity-catalog/ + ground-truth/
python3 build_scenario_folders.py          # expected-outputs/IPF-XXX_<path>/
python3 sync_demo_ingest.py                # refresh dataset-seed/cases/*/ingest/
```

## Key docs

- [`docs/HANDOFF.md`](docs/HANDOFF.md) — five-agent handoff map
- [`docs/TESTING_GUIDE.md`](docs/TESTING_GUIDE.md) — Fabric upload + e2e demo runbook
- [`docs/TEST_CASES.md`](docs/TEST_CASES.md) — scenario index
- [`docs/TEAM_HANDOFF.md`](docs/TEAM_HANDOFF.md) — structural change summary for integration teams
