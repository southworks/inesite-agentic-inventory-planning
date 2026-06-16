# inesite-agentic-inventory-planning

Reference implementation of the "Retail inventory planning and trend forecasting" use case
(Signal Ingestion → Feature & Causality → Forecasting → Replenishment & Allocation →
Planner Copilot, powered by Grok 4.3 in Azure AI Foundry).

Reference user story: [US 128593](https://dev.azure.com/southworks/inesite/_workitems/edit/128593).

## Dataset seed

[`dataset-seed/`](dataset-seed/) holds the synthetic-but-coherent retail dataset the agent
pipeline consumes. The Raw layer (`00_raw/`) is built first, from a curated real-data
extract — see [`dataset-seed/RAW_LAYER.md`](dataset-seed/RAW_LAYER.md) for the source data,
folder structure, and scenario coverage. Normalized JSON layers, policy RAG docs, and
ground truth are added in a follow-up step.