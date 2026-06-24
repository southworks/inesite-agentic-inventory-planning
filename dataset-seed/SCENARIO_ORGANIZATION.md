# Raw Layer organized by scenario — process & resulting organization

Why and how the Raw layer is organized **by end-to-end scenario / test case**, with a per-agent
sub-structure, plus narrative **demo flow stories** for starting a test run from any agent.
Parallel to the loan and R&D-knowledge repos' write-ups.

## The problem

`00_raw/` used to be organized **by format** (`00_raw/{csv,pdf,png,txt}/<source_type>/`). To
exercise one scenario you had to know which rows of which exports carried its signal, and there
was no place that showed *a whole workflow path* — the cases evaluated agents in isolation, not a
full chain. The goal: make each scenario a single, self-contained place an agent or demo can
point at, and let the demo **start at any agent** as if the previous ones had already run.

## Assessment — the raw↔scenario relationship differs per workflow

| Workflow | Model | Implication for a per-scenario layout |
|---|---|---|
| Loan | document-based — each applicant package belongs to one case | Clean partition, no duplication |
| **Inventory (this repo)** | **signal-based** — one system export carries signals for several scenarios (one `inventory_snapshot.csv` drives several cases; weekly POS batches span every SKU) | Needs a **canonical copy + sliced per-scenario duplicates** |
| R&D knowledge (hls) | entity-based — the same entity is cited by several cases | Canonical corpus + duplicated per-scenario folders |

Inventory is *signal-based*, so a strict per-scenario partition can't be lossless: the real
system exports aren't pre-sliced by analytical scenario. Forcing physical scenario folders means
**duplicating** (slicing) the exports — which is what is done, while keeping one canonical copy.

## Decision — e2e scenarios with a per-agent sub-structure

Each scenario is one full pass through the workflow, with a folder per agent/stage (the same
shape HLS uses for its multi-agent chain):

```
00_raw/
  _full_exports/<source_type>/        ← CANONICAL, un-sliced exports (single source of truth)
  IPF-XXX_<path>/                      ← one e2e scenario (trackable prefix + path)
    01_orchestrator/        request.json
    02_signal_ingestion/    agent_input.json  input/ (sliced raw + marquee pdf/png)  expected_output/ (01,02,03,04 entities)
    03_feature_causality/   agent_input.json  input/ (01 POS entities)               expected_output/ (05 demand signal)
    04_forecasting/         agent_input.json  input/ (05 demand signal)              expected_output/ (forecast + anomaly_flag)
    05_replenishment_allocation/ agent_input.json input/ (04 INV + 02 SUP)           expected_output/ (proposed order + expedite)
    06_planner_copilot/     agent_input.json  input/ (06 policy docs)               expected_output/ (approved order + outcome)
    scenario.json                              ← e2e rollup mirror of 07_decision_ground_truth/IPF-XXX.json
```

- Only `_full_exports/` + the normalized layers (`01_*`–`05_*`) + the rollups
  (`07_decision_ground_truth/IPF-XXX.json`) feed the build — the scenario folders are
  duplicates materialized from them, so they never affect the normalized layers or ground truth
  (**verified: regenerating leaves `01_*`–`04_*` byte-identical**).
- The scenario set is declared once in [`scenarios.py`](scenarios.py) (imported by
  `generate_normalized_layers.py` and `build_scenario_folders.py`), mirroring the
  `scenario_layout.py` shared-module pattern in loan.

See [HANDOFF.md](HANDOFF.md), [RAW_LAYER.md](RAW_LAYER.md), and [TEST_CASES.md](TEST_CASES.md).

## Demo flow stories

Each stage folder is self-contained: `agent_input.json` (the payload to start that agent),
`input/` (what it reads), and `expected_output/` (what it would produce). So a demo can start at
**any** agent and hand the next one a guaranteed output. Every story below has two parts: a
**conversational narrative** (how the data moves between agents in a real planning team) and a
**demo** block (the concrete files + the deliberately-varied entry point).

---

### Story A — `IPF-001` · Seasonal happy path · Gift Wrap Assortment @ Store TX-2

**Narrative.** It's mid-November and a retail demand planner in Texas wants to be sure the
*Gift Wrap Assortment* won't run dry over Christmas. She files a one-line planning request —
"plan replenishment for this SKU at TX-2 for the holiday weeks" — and the orchestrator fans it
out to the chain. The **Signal Ingestion agent** pulls the weekly POS exports and the inventory
snapshots from the systems of record and confirms they're clean (no gaps, no bad rows) — it hands
the next agent tidy weekly sales and stock numbers instead of raw CSV dumps. The **Feature &
Causality agent** looks at those weeks and adds context a human would: it tags which weeks are
holiday weeks, computes the 3-week rolling average and week-over-week change, and notes there's no
promotion in play — so the lift it sees is *organic* seasonal demand. The **Forecasting agent**
takes that feature-rich series and recognizes the real, steep Christmas ramp (the SKU more than
doubles into the peak week, ~208 units), and because nothing looks statistically off, it produces
a confident short-term forecast and passes it on. The **Replenishment agent** turns the forecast
into an order: it follows the holiday rule (stock to the holiday week's *own* expected demand, not
the quiet trailing average) and proposes an order sized to that peak. Finally the **Planner Copilot**
checks the order against budget and service-level policy, sees it sits comfortably under the cap,
and approves it automatically — no human needed. The planner gets a ready-to-place order with a
clear "why" attached to every step.

**Demo** — start at the top (`00_raw/IPF-001_seasonal_happy_path/`):

- **Start here:** `01_orchestrator/request.json` — the planning request scoped to the SKU/store.
- **Flow:** Signal Ingestion (validate POS/inventory) → Feature & Causality (holiday-week +
  rolling features) → Forecasting (real ramp, peak ~208, no anomaly) → Replenishment (size to the
  holiday week, `SN-500`/`RP-200`) → Planner Copilot (within budget & service-level).
- **Expected:** `final_outcome: order_approved`, 208 units, `required_human_review: false`.
- **Value:** the clean end-to-end happy path on *real* M5 demand — ingestion → features → forecast
  → plan → governance.

---

### Story B — `IPF-002` · Promotion spike → budget gate · Sparkling Water 12-Pack @ Store TX-2

**Narrative.** Marketing has booked a 20% discount on the *Sparkling Water 12-Pack* and expects a
big bump. The planning team needs to pre-buy enough to cover the promo without over-committing
cash. Here the interesting handoff is between **Feature & Causality** and the downstream agents:
the feature agent doesn't just see higher sales — it reads the promotions calendar, links the
declared promotion to this SKU/store/week, and measures the *elasticity* (how much the units
actually move per point of discount), passing along both the declared expected uplift (+60%) and
what the history suggests. The **Forecasting agent** uses the promo rule rather than the plain
baseline: the forecast becomes `baseline × (1 + uplift)` ≈ 584 units, well above a normal week, and
it flags this clearly as promo-driven so nobody mistakes it for organic growth. The **Replenishment
agent** sizes the order to that promotional forecast. Now the **Planner Copilot** does the job a
finance-minded planner would: it enforces the budget guardrail (an order may not exceed 3× the
SKU's average weekly demand). The promo order is large but still under that 1095-unit ceiling — so
the Copilot *routes it to a human for a quick budget sign-off* and, on approval, lets it through
within budget. The story shows the budget gate being exercised and reviewed, not rubber-stamped.

**Demo** — start mid-chain at Forecasting (`…/IPF-002_promotion_spike_budget_review/`), as if
ingestion + features already ran:

- **Start here:** `04_forecasting/agent_input.json` + `04_forecasting/input/DMD-FOODS_3_252.json`
  (the demand-signal features, including the promo/elasticity context).
- **Flow:** Forecasting (promo rule, ~584) → Replenishment (order to the uplifted forecast) →
  Planner Copilot (budget gate `BG-300`, human review, approved within the 1095 cap).
- **Expected:** `final_outcome: order_approved_within_budget`, 584 units,
  `binding_constraint: none`, `required_human_review: true`.
- **Value:** shows the promotion path and the Planner Copilot's budget enforcement with a
  human-in-the-loop sign-off.

---

### Story C — `IPF-003` · Supplier delay → stockout → expedite · Paper Towels 6-Roll @ Store TX-2

**Narrative.** A holiday-season carrier delay has thrown off the *Paper Towels 6-Roll* supply. The
demand itself is fine — people still buy paper towels at the usual rate — but the shelf is about to
go empty. This scenario is about **cross-feed reasoning**: the **Signal Ingestion agent** reconciles
*three* feeds — POS (demand steady), the inventory snapshots (on-hand sliding `86 → 0`, both below
the safety stock of 151), and the supplier shipment log (shipment `SHP-0003` ordered, *expected
Dec 5, actually landing Dec 19* — 14 days late, sitting `in_transit`). It even OCRs the receiving
report / packing slip PDF. The **Forecasting agent** confirms demand is on its normal baseline
(~151/week) — so this is unambiguously a *supply* problem, not a demand one, and it says so. The
**Replenishment agent** then makes the subtle call a good planner would: the missing units are
*already on the way* (the delayed shipment covers the gap), so placing a brand-new purchase order
would double-buy. Instead it raises **no new order** and flags the shipment to be **expedited**.
The **Planner Copilot** treats this as a service-level risk (the store could stock out before the
late truck arrives), enforces the `SL-100` gate, and escalates to a human to approve the expedite.
The handoff value is that each agent narrows the problem — demand-fine → supply-late → don't
re-order, just rush the truck — so the human gets a precise action, not "inventory is low."

**Demo** — start deep in the chain at Replenishment (`…/IPF-003_supplier_delay_stockout_expedite/`):

- **Start here:** `05_replenishment_allocation/input/` — the affected-week inventory snapshots
  (on-hand `86 → 0`, `BELOW_SAFETY_STOCK`) and supplier `SUP-003` with the 14-day-late `SHP-0003`.
- **Flow:** Replenishment (delayed qty already covers the gap → no new order, flag expedite) →
  Planner Copilot (service-level gate `SL-100`, human review).
- **Expected:** `final_outcome: expedite_required`, `proposed_order_qty: 0`,
  `binding_constraint: SL-100`, `required_human_review: true`.
- **Value:** start a demo deep in the chain without running upstream agents — the inventory
  analogue of the FSI "manual review" story, with cross-feed (inventory ⨯ supplier ⨯ demand)
  reasoning producing an explainable, policy-grounded action.

---

### Story D — `IPF-004` · Partial fill → stockout → reorder · Craft Paint Set @ Store CA-1

**Narrative.** The *Craft Paint Set* comes from an overseas vendor with a long lead time. This time
the shipment arrived *on schedule* — but the box was light: only 139 of the 240 ordered units
showed up (a 57.9% fill rate). This is the kind of discrepancy that hides in a clean dashboard but
is obvious on the dock. The **Signal Ingestion agent** reads the receiving report and packing-slip
scan and records the gap between ordered and received, and the inventory snapshot showing on-hand
down to 28 against a safety stock of 103. The **Forecasting agent** again confirms demand is
normal — the shortage is a supply shortfall, not a demand spike. Here the **Replenishment agent**
diverges from Story C: there's nothing extra in transit to cover the miss, so it *does* place a
follow-up order, sized by policy as `max(MOQ, shortfall)` — the vendor's 80-unit minimum order
quantity wins out over the smaller raw shortfall, so it recommends **80 units**. The **Planner
Copilot** checks that order against budget, finds it well within the cap, and approves it
automatically. The contrast with Story C is the point: same symptom (a stockout caused by a
supplier), but the right *action* differs — rush the existing truck vs. cut a new PO — and the
chain figures out which, and hands the planner the specific quantity.

**Demo** — start at the dock with Signal Ingestion (`…/IPF-004_partial_fill_stockout_reorder/`):

- **Start here:** `02_signal_ingestion/input/` — the raw supplier/inventory/POS slices plus the
  `SHP-0005` receiving report PDF + packing-slip PNG (the 57.9%-fill evidence to OCR).
- **Flow:** Signal Ingestion → Feature & Causality → Forecasting (baseline demand) → Replenishment
  (`max(MOQ, shortfall)` → 80) → Planner Copilot (within budget, auto-approved).
- **Expected:** `final_outcome: reorder_approved`, 80 units (= MOQ), `required_human_review: false`.
- **Value:** the multi-format OCR/vision entry point, and the supply-shortfall reorder path that
  contrasts with the expedite decision in Story C.

---

### Story E — `IPF-005` · Demand anomaly → no action · Puzzle 1000-Piece @ Store CA-1

**Narrative.** Something odd happens to the *Puzzle 1000-Piece* in California: for three days in
mid-December sales crater to `1, 1, 3` units in a week that otherwise reads in the teens and
forties. A naive system would either ignore it or panic-order. The valuable handoff here is
**Feature & Causality → Forecasting → Replenishment knowing when *not* to act**. The feature agent
computes a z-score on daily units and tags those days as a *statistical anomaly*, and — crucially —
finds **no explanation**: no promotion on the calendar, no holiday effect, no supplier disruption.
It passes the forecast agent an anomaly with no attached cause. The **Forecasting agent** therefore
refuses to bake the dip into the trend (it would be wrong to forecast permanently-low demand off
three weird days) and instead **routes the short-term trend to a human for review** — the "Short-term
trend" HITL gate in the diagram. The **Replenishment agent**, seeing a *demand-side* anomaly with no
supply gap, deliberately takes **no replenishment action** (ordering against noise would just create
excess stock). The outcome is "flagged for a human to look at, no order raised." The story makes the
point that a good agentic chain distinguishes a real signal from noise and escalates rather than
inventing a response.

**Demo** — start at Forecasting (`…/IPF-005_demand_anomaly_no_action/`):

- **Start here:** `04_forecasting/input/DMD-HOBBIES_1_268.json` — the demand signal whose
  `statistical_anomaly_weeks` flags the dip with empty promo/holiday/scenario context.
- **Flow:** Forecasting (anomaly detected, no cause → route to human) → Replenishment (no
  supply-side action, qty 0).
- **Expected:** `final_outcome: flagged_anomaly_no_action`, `anomaly_flag: true`,
  `proposed_order_qty: 0`, `required_human_review: true`.
- **Value:** the "know when not to act" path — anomaly detection + escalation instead of an
  automated (and wrong) order.

## Reproduce

```bash
cd dataset-seed
rm -rf 00_raw
python3 generate_raw_layer.py            # 00_raw/_full_exports/ canonical csv/txt
pip install -r requirements.txt
python3 generate_agent_documents.py      # pdf/png renderings into _full_exports/
python3 generate_normalized_layers.py    # 01-05 + 07 e2e rollups (from _full_exports/)
python3 build_scenario_folders.py        # 00_raw/IPF-XXX_<path>/<stage>/ per-agent folders
```
