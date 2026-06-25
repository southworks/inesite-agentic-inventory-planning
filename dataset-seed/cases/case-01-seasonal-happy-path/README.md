# Case 1 — Seasonal happy path

**User action:** Submit planning request via orchestrator (see `user_input.txt`).
**Ingest:** Upload `ingest/signal_ingestion/` then `ingest/forecasting/` to Fabric.
**Expected outcome:** Clean holiday forecast; order approved (208 units); no HITL gate.
**Legacy ID:** IPF-001

### User Input

Plan seasonal replenishment for Christmas week for SKU HOUSEHOLD_1_334 at store TX_2.
