# Raw Layer organized by scenario — process & resulting organization

Why and how the Raw layer was reorganized from *by format* to *by scenario / test case*, plus
narrative **demo flow stories** for starting a test run. Parallel to the loan and R&D-knowledge
repos' write-ups.

## The problem

`00_raw/` used to be organized **by format** (`00_raw/{csv,pdf,png,txt}/<source_type>/`). To
exercise one scenario you had to know which rows of which exports carried its signal. The goal:
make each scenario a single, self-contained place an agent or demo can point at.

## Assessment — the raw↔scenario relationship differs per workflow

| Workflow | Model | Implication for a per-scenario layout |
|---|---|---|
| Loan | document-based — each applicant package belongs to one case | Clean partition, no duplication |
| **Inventory (this repo)** | **signal-based** — one system export carries signals for several scenarios (one `inventory_snapshot.csv` drives STOCKOUT-01 *and* -02; weekly POS batches span every SKU) | Needs a **canonical copy + sliced per-scenario duplicates** |
| R&D knowledge (hls) | entity-based — the same entity is cited by several cases | Canonical corpus + duplicated per-scenario folders |

Inventory is *signal-based*, so a strict per-scenario partition can't be lossless: the real
system exports aren't pre-sliced by analytical scenario. Forcing physical scenario folders means
**duplicating** (slicing) the exports — which is what was done, while keeping one canonical copy.

## Decision

```
00_raw/
  _full_exports/<source_type>/   ← CANONICAL, un-sliced exports (single source of truth)
  <SCENARIO-ID>/<source_type>/   ← per-scenario SLICES (filtered to the case's SKU/store/window)
                                   + the marquee pdf/png documents for that case
```

- Only `_full_exports/` feeds the pipeline — `generate_normalized_layers.py` reads exclusively
  from there, so the slices never affect the normalized layers or ground truth (**verified:
  regenerating leaves `01_*`–`07_*` byte-identical**). This also fixed a latent breakage where
  the normalized reader pointed at paths the format-first reorg had moved.
- The `SCENARIOS` registry in [`generate_raw_layer.py`](generate_raw_layer.py) declares, per
  scenario, which source types + SKU(s)/store(s) each slice carries.

See [RAW_LAYER.md](RAW_LAYER.md) and [TEST_CASES.md](TEST_CASES.md).

## Demo flow stories

### Story A — "Clean Seasonal Forecast" (`SEASONAL-01`, Frozen Lasagna @ CA_1 + TX_2)

- **Start here:** `00_raw/SEASONAL-01/` — POS slices showing real dual holiday peaks
  (Thanksgiving + pre-Christmas) and a New-Year trough, plus inventory and a minor promo overlay.
- **Flow:** Signal Ingestion validates the weekly POS exports → Forecasting detects the seasonal
  shape (no anomaly) → Replenishment proposes orders sized to the peak using `SL-100`/`RP-200`
  policy via RAG.
- **Expected:** `07_decision_ground_truth/SEASONAL-01.json` — seasonal trend, no anomaly flag.
- **Value:** happy-path forecast on *real* M5 demand: ingestion → forecast → policy-grounded plan.

### Story B — "Holiday Stockout" (`STOCKOUT-01`, Paper Towels @ TX_2)

- **Start here:** `00_raw/STOCKOUT-01/` — `inventory_snapshots/inventory_snapshot.csv` shows
  on-hand falling `86 → 0` (both `BELOW_SAFETY_STOCK`) right before Christmas week, with the
  delayed shipment `SHP-0003` (14-day delay) sitting `in_transit`; the receiving report + packing
  slip and POS context are in the same folder.
- **Flow:** Signal Ingestion reconciles inventory + supplier feeds → Forecasting sees demand
  intact → Replenishment flags stockout risk and recommends expedite, tracing the cause to
  `SUPPLIER-DELAY-01` (`SP-410` policy).
- **Expected:** `07_decision_ground_truth/STOCKOUT-01.json` — `stockout_risk_pending_delayed_shipment`,
  `expedite_required: true`.
- **Value:** cross-feed reasoning (inventory ⨯ supplier ⨯ demand) producing an explainable,
  policy-grounded action — the inventory analogue of the FSI "manual review" story.

## Reproduce

```bash
cd dataset-seed
rm -rf 00_raw
python3 generate_raw_layer.py
python3 generate_normalized_layers.py
pip install -r requirements.txt
python3 generate_agent_documents.py
```
