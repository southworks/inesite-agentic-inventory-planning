# Case 3 — Supplier delay → expedite

**User action:** Submit planning request via orchestrator (see `user_input.txt`).
**Ingest:** Upload `ingest/signal_ingestion/` (includes supplier shipments) then `ingest/forecasting/` to Fabric.
**Expected outcome:** Expedite required (qty 0, in-transit covers); planner service-level HITL.
**Legacy ID:** IPF-003

### User Input

Assess stockout risk from supplier delay for SKU HOUSEHOLD_1_447 at store TX_2.
