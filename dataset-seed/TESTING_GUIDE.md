# Demo Runbook — running each scenario end-to-end

A guide for driving a **full five-agent demo** of the inventory-planning workflow using the
documents under `00_raw/IPF-XXX_<path>/`. Only **Signal Ingestion** and **Forecasting** have
dataset folders (they consume files via MCP); the other agents run on **workflow memory** —
their expected handoffs are in `scenario.json`.

See [TEST_CASES.md](TEST_CASES.md) for the case index, [HANDOFF.md](HANDOFF.md) for the
handoff map, and [SCENARIO_ORGANIZATION.md](SCENARIO_ORGANIZATION.md) for the narrative stories.

## Prerequisites — data in Fabric before you run a scenario

The files under each stage's `input/` folder are **not** read from the repo at runtime. They
represent the retail signals that must **already be uploaded and available** in the data
platform so agents can **query them via MCP tools** during the demo.

For demos, that platform is **Microsoft Fabric** — typically a **Lakehouse** (or equivalent
OneLake-backed storage) scoped per scenario or per environment. Before triggering a scenario:

1. **Upload** the contents of `01_signal_ingestion/input/` (POS/inventory/supplier csv/txt,
   marquee pdf/png) and `02_forecasting/input/` (`DMD-{sku}.json`, `seasonal_planning_policy.txt`)
   into the target Fabric Lakehouse, preserving paths or naming conventions your MCP tools expect.
2. **Wire MCP tools** so Signal Ingestion and Forecasting can list/read those objects (not the
   local `dataset-seed/` tree).
3. **Keep locally** `agent_input.json`, `expected_output/`, and `scenario.json` for orchestration
   and validation — those are the answer key and stage payloads, not systems-of-record data.

Without this upload step, agents have nothing to ingest and the scenario cannot be exercised
end-to-end, even if `00_raw/` is present in the repo.

## How a scenario folder is laid out

Every scenario lives in one folder, e.g. `00_raw/IPF-001_seasonal_happy_path/`:

```
scenario.json                 orchestrator_request + stages[] for ALL five agents (answer key)

01_signal_ingestion/          ← MCP stage (data lives in Fabric; see Prerequisites)
  agent_input.json
  input/                      ← upload to Lakehouse: raw csv/txt + marquee pdf/png
  expected_output/            normalized entities + _expected_output.json (validation only)

02_forecasting/               ← MCP stage (data lives in Fabric; see Prerequisites)
  agent_input.json
  input/                      ← upload to Lakehouse: DMD-{sku}.json + seasonal_planning_policy.txt
  expected_output/            forecast_result.json + _expected_output.json (validation only)
```

There are **no** per-agent folders for Orchestrator, Feature & Causality, Replenishment, or
Planner Copilot. Those stages use `scenario.json` → `stages[]` for `agent_input`, expected
`decision`, and `expected_output`.

## How to run a full scenario

1. **Prerequisite** — upload the scenario's `input/` files to Microsoft Fabric (Lakehouse);
   confirm MCP tools can read them.
2. **Trigger** — pass `scenario.json` → `orchestrator_request` to the Planning orchestrator.
3. **Run agents in order** — Signal Ingestion → Feature & Causality → Forecasting →
   Replenishment → Planner Copilot.
4. **MCP stages** query Fabric at steps 1 and 3; pass prior agents' structured outputs through
   workflow memory for the rest.
5. **Validate** each stage against `scenario.json` → `stages[]` (or the materialized
   `expected_output/` where it exists).

| Step | Agent | Data from Fabric (via MCP)? | Validate against |
|------|-------|-----------------------------|------------------|
| 0 | Orchestrator | — | `scenario.json` → `orchestrator_request` |
| 1 | Signal Ingestion | **yes** — objects from `01_signal_ingestion/input/` | `01_signal_ingestion/expected_output/` |
| 2 | Feature & Causality | no — uses ingestion output in memory | `scenario.json` → `stages[feature_causality]` |
| 3 | Forecasting | **yes** — objects from `02_forecasting/input/` | `02_forecasting/expected_output/` |
| 4 | Replenishment | no — uses forecast in memory | `scenario.json` → `stages[replenishment_allocation]` |
| 5 | Planner Copilot | no — uses replenishment plan in memory | `scenario.json` → `final_outcome` |

> **Forecasting in isolation:** point MCP at `02_forecasting/input/` in Fabric — the `DMD` file
> represents Feature & Causality having already run.

---

## IPF-001 — Seasonal happy path *(Gift Wrap Assortment @ Store TX-2)*

**The situation.** Christmas week demand ramps steeply. Clean end-to-end happy path.

**Trigger:** `orchestrator_request.intent` = `plan_seasonal_replenishment_for_christmas_week`

**Full flow:**

1. **Orchestrator** — `scenario.json` → `orchestrator_request`.
2. **Signal Ingestion** → MCP reads `01_signal_ingestion/input/` from Fabric (POS + inventory csv, `SUP-004_profile.pdf`).
3. **Feature & Causality** — tags Christmas week as **holiday week** (no promo).
   *Expected:* `features_built` — see `scenario.json` stages[feature_causality].
4. **Forecasting** → MCP reads `02_forecasting/input/` from Fabric (DMD + `seasonal_planning_policy.txt`).
   *Expected:* **208 units** for `TX_2|2015-12-21`, `anomaly_flag: false`.
5. **Replenishment** — order **208 units**, no expedite.
6. **Planner Copilot** — auto-approved, within budget.

**Final outcome:** `order_approved` — 208 units, no human review.

---

## IPF-002 — Promotion spike, budget review *(Sparkling Water 12-Pack @ Store TX-2)*

**Trigger:** `plan_replenishment_for_declared_promotion`

1. **Orchestrator** — `scenario.json` → `orchestrator_request`.
2. **Signal Ingestion** → `01_signal_ingestion/input/` (POS + **promo calendar** + inventory + `PROMO-2015-11-A.pdf`).
3. **Feature & Causality** — tags **promo week**, measures uplift.
4. **Forecasting** → `02_forecasting/` — forecast **584 units** (SN-510), no anomaly.
5. **Replenishment** — recommends **584 units**.
6. **Planner Copilot** — under budget cap (1095) but **routes to human** for budget review.

**Final outcome:** `order_approved_within_budget` — 584 units, human review on budget gate.

---

## IPF-003 — Supplier delay → stockout → expedite *(Paper Towels @ Store TX-2)*

**Trigger:** `assess_stockout_risk_from_supplier_delay`

1. **Orchestrator** — `scenario.json` → `orchestrator_request`.
2. **Signal Ingestion** → `01_signal_ingestion/input/` (POS + inventory + supplier txt, `SHP-0003` PDF/PNG, `SUP-003_profile.pdf`).
3. **Feature & Causality** — normal demand features, no promo/anomaly.
4. **Forecasting** → `02_forecasting/` — baseline **151 units/week**, no anomaly.
5. **Replenishment** — **no new order** (`proposed_order_qty: 0`), flag **expedite**.
6. **Planner Copilot** — **escalates to human** (service-level gate SL-100).

**Final outcome:** `expedite_required` — no new order, human review on service-level gate.

---

## IPF-004 — Partial fill → stockout → reorder *(Craft Paint Set @ Store CA-1)*

**Trigger:** `assess_stockout_risk_from_partial_fill`

1. **Orchestrator** — `scenario.json` → `orchestrator_request`.
2. **Signal Ingestion** → `01_signal_ingestion/input/` (POS + inventory + supplier, `SHP-0005` PDF/PNG showing 57.9% fill).
3. **Feature & Causality** — normal demand features.
4. **Forecasting** → `02_forecasting/` — baseline **103 units/week**.
5. **Replenishment** — shortfall 25, MOQ wins → order **80 units**.
6. **Planner Copilot** — auto-approved within budget.

**Final outcome:** `reorder_approved` — 80 units, no human review.

---

## IPF-005 — Unexplained demand anomaly *(Puzzle 1000-Piece @ Store CA-1)*

**Trigger:** `investigate_demand_anomaly`

1. **Orchestrator** — `scenario.json` → `orchestrator_request`.
2. **Signal Ingestion** → `01_signal_ingestion/input/` (POS + inventory; dip `1,1,3` visible in normalized POS).
3. **Feature & Causality** — marks **statistical anomaly**, no explaining driver.
4. **Forecasting** → `02_forecasting/` — baseline **99 units/week**, **`anomaly_flag: true`**, routes to human.
5. **Replenishment** — **no supply action** (`proposed_order_qty: 0`).
6. **Planner Copilot** — records flag for review.

**Final outcome:** `flagged_anomaly_no_action` — no order, human review at Forecasting.

---

## At-a-glance

| Scenario | Product @ store | MCP folders used | Final outcome | Human review? |
|---|---|---|---|---|
| `IPF-001` | Gift Wrap @ TX-2 | `01_signal_ingestion`, `02_forecasting` | `order_approved` (208 u) | no |
| `IPF-002` | Sparkling Water @ TX-2 | same | `order_approved_within_budget` (584 u) | yes — budget |
| `IPF-003` | Paper Towels @ TX-2 | same | `expedite_required` (no new order) | yes — service level |
| `IPF-004` | Craft Paint @ CA-1 | same | `reorder_approved` (80 u) | no |
| `IPF-005` | Puzzle @ CA-1 | same | `flagged_anomaly_no_action` (no order) | yes — forecasting |

Each scenario's `scenario.json` is the full answer key for all five agents.
