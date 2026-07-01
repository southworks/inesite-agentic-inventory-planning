# Scenario Organization

Each scenario is a self-contained runtime case under `dataset-seed/cases/`.

| Runtime case | Legacy ID | Role |
| --- | --- | --- |
| `case-01` | `IPF-001` | Seasonal happy path |
| `case-02` | `IPF-002` | Promotion spike with budget review |
| `case-03` | `IPF-003` | Supplier delay and expedite |
| `case-04` | `IPF-004` | Partial fill and reorder |
| `case-05` | `IPF-005` | Demand anomaly and no action |

Each case contains `ingest/`, `fabric-pre-requisite-data/`, and `README.md`. The UI catalog is `dataset-seed/cases/catalog.json`.

Scenario definitions and expected handoffs live in `data-generation/scripts/scenarios.py` and `data-generation/ground-truth/`.

## How to add a scenario

See [`../README.md`](../README.md#how-to-add-a-scenario).
