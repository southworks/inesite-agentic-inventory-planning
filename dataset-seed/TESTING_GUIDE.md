# Demo Runbook — running each scenario by injecting the prepared documents

A high-level guide for the development team to **drive a live demo** of the inventory-planning
agent chain using the documents already prepared under `00_raw/IPF-XXX_<path>/`. You don't need to
generate anything (the dataset ships ready) or run any terminal commands. Each scenario is a folder
you feed to the agents, one stage at a time, and watch the chain produce the expected result.

See [TEST_CASES.md](TEST_CASES.md) for the case index and [HANDOFF.md](HANDOFF.md) for the precise
agent-to-agent handoff map.

## How a scenario folder is laid out

Every scenario lives in one folder, e.g. `00_raw/IPF-003_supplier_delay_stockout_expedite/`, with
one sub-folder per agent in run order:

```
01_orchestrator/            the planning request that kicks off the run
02_signal_ingestion/        ┐
03_feature_causality/       │  one folder per agent, each containing:
04_forecasting/             │    • agent_input.json   → the payload that STARTS this agent
05_replenishment_allocation/│    • input/             → the documents to FEED the agent
06_planner_copilot/         ┘    • expected_output/    → what the agent SHOULD produce
scenario.json               the full expected end-to-end result (the answer key)
```

## How to run a scenario

For each agent, in order:

1. **Inject** the agent with `agent_input.json` and the files in that stage's `input/` folder.
2. **Observe** what the agent does — the runbook below tells you what to expect in plain terms.
3. **Compare** the agent's result against that stage's `expected_output/` folder — it should look
   the same (same decision, same numbers).
4. **Hand off** to the next agent: a stage's `expected_output/` is exactly the next stage's
   `input/`, so the chain flows on its own.

> **Start anywhere.** You don't have to start at the first agent. To open the demo mid-chain (e.g.
> straight at Forecasting), just inject that stage's `input/` — it already contains the documents
> the upstream agents *would* have produced. So you can showcase any single agent in isolation and
> still get the right result.

---

## IPF-001 — Seasonal happy path *(Gift Wrap Assortment @ Store TX-2)*

**The situation.** It's December. Gift wrap sales ramp steeply into Christmas week. Nothing is
broken — this is the clean, end-to-end "everything works" story.

**The data flow, in plain terms.** The store's weekly sales feed comes in and is cleaned up. The
system notices Christmas week is a holiday spike, not random noise, so it forecasts demand off the
holiday level instead of the calm-week average. It sizes a purchase order to cover that spike,
checks it's affordable and won't break service levels, and approves it automatically. No human
needed.

**Step by step:**

1. **Orchestrator** — `01_orchestrator/request.json` asks to plan replenishment for Christmas week.
2. **Signal Ingestion** → inject `02_signal_ingestion/input/` (weekly POS + inventory exports). The
   agent validates the feeds and normalizes them. *Result resembles* `02_signal_ingestion/expected_output/`
   (clean per-week sales and stock records). → next.
3. **Feature & Causality** → inject `03_feature_causality/input/`. The agent builds the demand
   features and tags Christmas week as a **holiday week**. *Result:* `03_feature_causality/expected_output/`
   (`DMD-HOUSEHOLD_1_334` with rolling averages + holiday flags). → next.
4. **Forecasting** → inject `04_forecasting/input/`. The agent forecasts **208 units** for Christmas
   week (off the holiday level) and flags **no anomaly**. *Result:* `04_forecasting/expected_output/`. → next.
5. **Replenishment & Allocation** → inject `05_replenishment_allocation/input/`. The agent recommends
   an order of **208 units** (no shortfall, no expedite). *Result:* `05_replenishment_allocation/expected_output/`. → next.
6. **Planner Copilot** → inject `06_planner_copilot/input/`. The agent checks the order against the
   budget cap (333) and service level — it's within both — and **approves automatically**. *Result:*
   `06_planner_copilot/expected_output/`.

**Final outcome:** `order_approved` — 208 units, no human review. (Matches `scenario.json`.)

---

## IPF-002 — Promotion spike, budget review *(Sparkling Water 12-Pack @ Store TX-2)*

**The situation.** A 20%-off promotion is scheduled. Demand will jump well above the normal week,
so the order is large enough that a human signs off on the spend before it goes out.

**The data flow, in plain terms.** Sales and the promo calendar come in together. The system
recognizes the discount week and measures how much extra people actually bought (the promo's
"lift"). It forecasts the uplifted demand, sizes a bigger-than-usual order to match, and — because
it's a promotional spend — routes it through the budget gate for a human to review. The spend is
under the cap, so the reviewer approves it.

**Step by step:**

1. **Orchestrator** — `01_orchestrator/request.json` asks to plan for the declared promotion.
2. **Signal Ingestion** → inject `02_signal_ingestion/input/` (POS + **promo calendar** + inventory).
   *Result:* `02_signal_ingestion/expected_output/` (includes the `PROMO-2015-11-A` event). → next.
3. **Feature & Causality** → inject `03_feature_causality/input/`. The agent tags the **promo week**
   and measures the observed uplift (elasticity). *Result:* `03_feature_causality/expected_output/`. → next.
4. **Forecasting** → inject `04_forecasting/input/`. The agent forecasts **584 units** (the ~60%
   uplifted demand), no anomaly. *Result:* `04_forecasting/expected_output/`. → next.
5. **Replenishment & Allocation** → inject `05_replenishment_allocation/input/`. The agent recommends
   **584 units** to cover the promo. *Result:* `05_replenishment_allocation/expected_output/`. → next.
6. **Planner Copilot** → inject `06_planner_copilot/input/`. The order is under the budget cap (1095)
   but elevated, so the agent **routes it to a human for budget review** — and it's approved. *Result:*
   `06_planner_copilot/expected_output/`.

**Final outcome:** `order_approved_within_budget` — 584 units, **human review** on the budget gate.

---

## IPF-003 — Supplier delay → stockout → expedite *(Paper Towels 6-Roll @ Store TX-2)*

**The situation.** A shipment is running 14 days late, right before the Christmas rush. The store is
about to run dry. This is a *supply* problem, not a demand problem — the fix is to rush the truck,
not to order more.

**The data flow, in plain terms.** The sales feed looks normal, but the supplier feed shows the
shipment is two weeks late and the inventory feed shows stock dropping to zero, below the safety
buffer. The system forecasts the usual demand (demand is fine), then sees the missing stock is
*already on its way* — so instead of placing a second, wasteful order, it flags an **expedite** and
escalates to a human to enforce the service level.

**Step by step:**

1. **Orchestrator** — `01_orchestrator/request.json` asks to assess stockout risk from a supplier delay.
2. **Signal Ingestion** → inject `02_signal_ingestion/input/` (POS + inventory + **supplier feed**,
   plus the `SHP-0003` receiving report PDF and packing-slip PNG for the OCR/vision angle). The agent
   validates everything. *Result:* `02_signal_ingestion/expected_output/` (includes `SUP-003`). → next.
3. **Feature & Causality** → inject `03_feature_causality/input/`. Demand features look normal — no
   promo, no anomaly. *Result:* `03_feature_causality/expected_output/`. → next.
4. **Forecasting** → inject `04_forecasting/input/`. The agent forecasts the **baseline 151 units/week**
   and flags **no anomaly** (demand is healthy). *Result:* `04_forecasting/expected_output/`. → next.
5. **Replenishment & Allocation** → inject `05_replenishment_allocation/input/` (the at-risk inventory
   snapshots + `SUP-003`). The agent sees the gap is already covered by the in-transit shipment, so it
   recommends **no new order** and **flags an expedite**. *Result:* `05_replenishment_allocation/expected_output/`
   (`proposed_order_qty: 0`, `expedite_required: true`). → next.
6. **Planner Copilot** → inject `06_planner_copilot/input/`. The agent enforces the **service-level**
   policy (SL-100) and **escalates to a human** to authorize the expedite. *Result:* `06_planner_copilot/expected_output/`.

**Final outcome:** `expedite_required` — no new order, **human review** on the service-level gate.

---

## IPF-004 — Partial fill → stockout → reorder *(Craft Paint Set @ Store CA-1)*

**The situation.** A shipment arrived on time but only **57.9% full** — the vendor short-shipped.
Stock dips below the safety buffer, so the system places a small follow-up order to cover the gap.

**The data flow, in plain terms.** The supplier feed shows the delivery came in light (139 of 240
units). The inventory feed shows stock below safety. Demand is normal. The system orders just the
shortfall — but the supplier's minimum order quantity (80) is bigger than the gap (25), so the order
rounds up to the minimum. It's cheap and within budget, so it's approved automatically.

**Step by step:**

1. **Orchestrator** — `01_orchestrator/request.json` asks to assess stockout risk from a partial fill.
2. **Signal Ingestion** → inject `02_signal_ingestion/input/` (POS + inventory + **supplier feed**,
   plus `SHP-0005`'s receiving PDF + packing-slip PNG showing the short count). *Result:*
   `02_signal_ingestion/expected_output/` (includes `SUP-005`). → next.
3. **Feature & Causality** → inject `03_feature_causality/input/`. Demand features are normal.
   *Result:* `03_feature_causality/expected_output/`. → next.
4. **Forecasting** → inject `04_forecasting/input/`. The agent forecasts the **baseline 103 units/week**,
   no anomaly. *Result:* `04_forecasting/expected_output/`. → next.
5. **Replenishment & Allocation** → inject `05_replenishment_allocation/input/`. The shortfall is 25
   units, but the minimum order quantity is 80, so the agent recommends **80 units**. *Result:*
   `05_replenishment_allocation/expected_output/` (`shortfall_units: 25`, `proposed_order_qty: 80`). → next.
6. **Planner Copilot** → inject `06_planner_copilot/input/`. The order is small and within budget (cap
   309), so it's **approved automatically**; the binding constraint is the MOQ. *Result:*
   `06_planner_copilot/expected_output/`.

**Final outcome:** `reorder_approved` — 80 units, no human review.

---

## IPF-005 — Unexplained demand anomaly *(Puzzle 1000-Piece @ Store CA-1)*

**The situation.** Sales suddenly collapse to almost nothing for three days (1, 1, 3 units) with no
promotion, no holiday, and no supplier issue to explain it. The right move is to **flag it for a
human**, not to react to it as if it were real demand.

**The data flow, in plain terms.** The sales feed shows a sharp, unexplained dip. The system's
feature step catches it statistically, but there's nothing in the promo calendar, the holiday
calendar, or the supplier feed to account for it. So the forecasting agent refuses to quietly smooth
it away — it raises an **anomaly flag** and routes the short-term trend to a human. Because it's
demand-side noise (not a real supply gap), no order is placed.

**Step by step:**

1. **Orchestrator** — `01_orchestrator/request.json` asks to investigate a demand anomaly.
2. **Signal Ingestion** → inject `02_signal_ingestion/input/` (POS + inventory). *Result:*
   `02_signal_ingestion/expected_output/` (the dip is present in the normalized sales). → next.
3. **Feature & Causality** → inject `03_feature_causality/input/`. The agent computes the demand
   features and marks the week as a **statistical anomaly**. *Result:* `03_feature_causality/expected_output/`. → next.
4. **Forecasting** → inject `04_forecasting/input/`. With nothing to explain the dip, the agent
   forecasts the **baseline 99 units/week**, **raises the anomaly flag**, and **routes to a human**.
   *Result:* `04_forecasting/expected_output/` (`anomaly_flag: true`). → next.
5. **Replenishment & Allocation** → inject `05_replenishment_allocation/input/`. Because the anomaly
   is demand-side noise, the agent takes **no supply action** (`proposed_order_qty: 0`). *Result:*
   `05_replenishment_allocation/expected_output/`. → next.
6. **Planner Copilot** → inject `06_planner_copilot/input/`. Nothing to spend, so it records the
   **flag for review** and closes out. *Result:* `06_planner_copilot/expected_output/`.

**Final outcome:** `flagged_anomaly_no_action` — no order, **human review** raised at Forecasting.

---

## At-a-glance: what each scenario should end with

| Scenario | Product @ store | What it demonstrates | Final outcome | Human review? |
|---|---|---|---|---|
| `IPF-001` | Gift Wrap @ TX-2 | clean seasonal happy path | `order_approved` (208 u) | no |
| `IPF-002` | Sparkling Water @ TX-2 | promo uplift + budget gate | `order_approved_within_budget` (584 u) | yes — budget |
| `IPF-003` | Paper Towels @ TX-2 | supplier delay → expedite | `expedite_required` (no new order) | yes — service level |
| `IPF-004` | Craft Paint @ CA-1 | partial fill → reorder | `reorder_approved` (80 u) | no |
| `IPF-005` | Puzzle @ CA-1 | unexplained anomaly | `flagged_anomaly_no_action` (no order) | yes — forecasting |

Each scenario's `scenario.json` is the full answer key for that run.
