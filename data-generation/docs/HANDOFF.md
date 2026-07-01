# Agent Handoff

The inventory workflow is a five-agent chain:

`signal-ingestion-agent` -> `feature-and-causality-agent` -> `forecasting-agent` -> `replenishment-and-allocation-agent` -> `planner-copilot-agent`.

## Runtime data package

Runtime case data lives under:

```text
dataset-seed/cases/{caseId}/
  ingest/                       flat files exposed by API document endpoints
  fabric-pre-requisite-data/    case-scoped normalized JSON read by MCP tools
  README.md
```

Case metadata for the UI lives in `dataset-seed/cases/catalog.json`.

## Handoff chain

| # | Agent | Receives | Runtime data | Produces |
| --- | --- | --- | --- | --- |
| 1 | Signal Ingestion | request scope and source list | `fabric-pre-requisite-data/` plus workflow memory | validated POS, inventory, supplier, and promotion signals |
| 2 | Feature & Causality | validated signals | workflow memory | predictors, events, and causal features |
| 3 | Forecasting | scope and features | workflow memory plus generated demand signal context | short-term forecast and anomaly flags |
| 4 | Replenishment & Allocation | forecast and inventory context | workflow memory | proposed order, reorder, or expedite action |
| 5 | Planner Copilot | replenishment plan | workflow memory and policy context | final planner decision and HITL routing |

The full expected handoff for each case is captured in `data-generation/ground-truth/IPF-XXX.json`. The runtime application does not read per-stage scenario folders.

## How to add a scenario

See [`../README.md`](../README.md#how-to-add-a-scenario).
