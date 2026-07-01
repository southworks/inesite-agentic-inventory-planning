#!/usr/bin/env python3
"""
End-to-end scenario definitions for the retail Agentic inventory-planning dataset.

Single source of truth for the e2e *test cases*. Each scenario is one full path
through the workflow (Orchestrator -> Signal Ingestion -> Feature & Causality ->
Forecasting -> Replenishment & Allocation -> Planner Copilot, powered by Grok 4.3)
and differs from the others at the human-in-the-loop gates.

Both generators import this module so the ground-truth rollups and runtime case
folders stay aligned:

  - generate_normalized_layers.py  -> ground-truth/IPF-XXX.json (e2e rollup)
  - build_case_folders.py            -> dataset-seed/cases/case-XX/ ingest + fabric-pre-requisite-data

Mirrors the `scenarios.py` shared-module pattern used by loan-mortgage and HLS,
adapted to this 5-agent chain. Because the workflow is signal-based, the canonical
exports in `data-generation/corpus/` stay the generation source and runtime case
folders carry sliced copies of them.

Trackable prefix: IPF-### (Inventory Planning & Forecasting), like APP-XXX in loan
and RKM-XXX in HLS.

Each scenario declares:
  - scenario_id / path / title / final_outcome / required_human_review
  - anchor          : the signal anchor + reference-computation inputs (sku/stores/weeks/type),
                      consumed by generate_normalized_layers.py to COMPUTE the numeric outputs
  - raw_slice       : which canonical source types + sku/store/supplier/promo ids the
                      Signal-Ingestion ingest/ slice carries (build_case_folders.py)
  - orchestrator_request : the optional Planning-request that the orchestrator routes
  - stages          : the 5 agent stages, each with the structured agent_input that STARTS
                      that agent in isolation, plus its HITL gate and policy refs. The numeric
                      expected_output for forecasting/replenishment/planner is computed by
                      generate_normalized_layers.py (not hand-asserted here).

The numbers are calculable from the canonical corpus plus policy thresholds —
generate_normalized_layers.py is the reference implementation of that calculation.
"""

from __future__ import annotations

# Historical stage labels kept for ground-truth metadata.
STAGE_FOLDERS = {
    "signal_ingestion": "01_signal_ingestion",
    "forecasting": "02_forecasting",
}

# Subset of the e2e chain materialized by build_case_folders.py.
SCENARIO_FOLDER_STAGES = ["signal_ingestion", "forecasting"]

# The agent capabilities, in chain order (used for rollup `routed_to`).
AGENT_CHAIN = [
    "signal_ingestion_agent",
    "feature_causality_agent",
    "forecasting_agent",
    "replenishment_allocation_agent",
    "planner_copilot_agent",
]

WINDOW = ["2015-10-19", "2016-01-03"]
POLICY_REFS_PLANNER = ["SL-100", "BG-300"]


def _signal_ingestion_stage(scope: dict, sources: list) -> dict:
    return {
        "stage": "signal_ingestion", "agent": "signal_ingestion_agent",
        "agent_input": {
            "task": "ingest_real_time_signals_and_validate_quality",
            "sources": sources,
            "scope": scope,
            "window": WINDOW,
        },
        "gate": "quality_validated",  # "Validate quality" HITL — passes for every seed case
        "policy_refs": [],
    }


def _feature_causality_stage(scope: dict, has_promo: bool) -> dict:
    return {
        "stage": "feature_causality", "agent": "feature_causality_agent",
        "agent_input": {
            "task": "build_events_select_predictors_and_test_elasticities",
            "scope": scope,
            "predictors": ["rolling_3wk_avg", "pct_change_vs_prior_week"],
            "events": ["promo_weeks", "holiday_weeks", "statistical_anomaly_weeks"],
            "test_elasticity": has_promo,
        },
        "gate": None,
        "policy_refs": ["SN-500", "SN-510"] if has_promo else ["SN-500"],
    }


def _forecasting_stage(scope: dict, anomaly: bool) -> dict:
    return {
        "stage": "forecasting", "agent": "forecasting_agent",
        "agent_input": {
            "task": "forecast_short_term_trend_and_detect_shifts",
            "scope": scope,
            "horizon": "weekly",
        },
        # "Short-term trend" HITL: only the unexplained-anomaly path is routed to a human.
        "gate": "anomaly_review" if anomaly else None,
        "policy_refs": ["SN-500", "SN-510"],
    }


def _replenishment_stage(scope: dict, refs: list) -> dict:
    return {
        "stage": "replenishment_allocation", "agent": "replenishment_allocation_agent",
        "agent_input": {
            "task": "recommend_targets_and_orders_and_create_pos_tos",
            "scope": scope,
        },
        "gate": None,
        "policy_refs": [r for r in refs if r.startswith(("RP-", "SP-", "SL-"))] or ["RP-200"],
    }


def _planner_stage(scope: dict, hitl: bool) -> dict:
    return {
        "stage": "planner_copilot", "agent": "planner_copilot_agent",
        "agent_input": {
            "task": "enforce_service_level_and_budget",
            "scope": scope,
            "policy_refs": POLICY_REFS_PLANNER,
        },
        # "Enforce budget" / service-level HITL.
        "gate": "human_review" if hitl else "auto_approved",
        "policy_refs": POLICY_REFS_PLANNER,
    }


def _make(scenario_id, path, title, final_outcome, required_human_review,
          anchor, raw_slice, request_intent, *, has_promo, anomaly, planner_hitl):
    scope = {
        "sku_id": anchor["sku"], "store_ids": anchor["stores"],
        "affected_weeks": anchor["weeks"],
    }
    return {
        "scenario_id": scenario_id,
        "path": path,
        "title": title,
        "final_outcome": final_outcome,
        "required_human_review": required_human_review,
        "anchor": anchor,
        "raw_slice": raw_slice,
        "orchestrator_request": {
            "request_id": f"{scenario_id}-REQ",
            "intent": request_intent,
            "scope": {"sku_id": anchor["sku"], "store_ids": anchor["stores"],
                      "campaigns": raw_slice.get("promo_events", [])},
            "horizon": "short_term_weekly",
            "routed_to": AGENT_CHAIN,
        },
        "stages": [
            _signal_ingestion_stage(scope, raw_slice["sources"]),
            _feature_causality_stage(scope, has_promo),
            _forecasting_stage(scope, anomaly),
            _replenishment_stage(scope, anchor["refs"]),
            _planner_stage(scope, planner_hitl),
        ],
    }


SCENARIOS: list[dict] = [
    # ------------------------------------------------------------------ IPF-001
    _make(
        "IPF-001", "seasonal_happy_path",
        "Seasonal Christmas ramp — clean forecast and replenishment (happy path)",
        "order_approved", False,
        anchor={
            "type": "seasonal_trend", "sku": "HOUSEHOLD_1_334", "stores": ["TX_2"],
            "weeks": ["2015-12-21"],
            "reason": "seasonal_holiday_demand_peak",
            "refs": ["SN-500", "SL-100", "RP-200", "BG-300"],
            "summary": "Real, steep ramp into Christmas week at TX_2; forecast off the holiday "
                       "week's own historical level (SN-500), order sized to it, within budget and "
                       "service-level — no human review.",
        },
        raw_slice={"skus": ["HOUSEHOLD_1_334"], "stores": ["TX_2"],
                   "suppliers": ["SUP-004"], "shipments": [], "promo_events": [],
                   "sources": ["pos_transactions", "inventory_snapshots"]},
        request_intent="plan_seasonal_replenishment_for_christmas_week",
        has_promo=False, anomaly=False, planner_hitl=False,
    ),
    # ------------------------------------------------------------------ IPF-002
    _make(
        "IPF-002", "promotion_spike_budget_review",
        "Promotion demand spike — uplifted forecast routed through the budget gate",
        "order_approved_within_budget", True,
        anchor={
            "type": "promotion_demand_spike", "sku": "FOODS_3_252", "stores": ["TX_2"],
            "weeks": ["2015-11-02"],
            "reason": "promotional_demand_uplift",
            "refs": ["SN-510", "RP-200", "BG-300"],
            "summary": "20% discount, declared expected_uplift_pct=60; forecast = baseline x (1 + "
                       "uplift) per SN-510. The elevated order is routed through the Planner Copilot "
                       "budget gate (BG-300) for human review and approved within the 3x cap.",
        },
        raw_slice={"skus": ["FOODS_3_252"], "stores": ["TX_2"],
                   "suppliers": ["SUP-002"], "shipments": [], "promo_events": ["PROMO-2015-11-A"],
                   "sources": ["pos_transactions", "promotions", "inventory_snapshots"]},
        request_intent="plan_replenishment_for_declared_promotion",
        has_promo=True, anomaly=False, planner_hitl=True,
    ),
    # ------------------------------------------------------------------ IPF-003
    _make(
        "IPF-003", "supplier_delay_stockout_expedite",
        "Supplier delay drives a stockout — expedite routed through the service-level gate",
        "expedite_required", True,
        anchor={
            "type": "stockout_risk", "sku": "HOUSEHOLD_1_447", "stores": ["TX_2"],
            "weeks": ["2015-12-07", "2015-12-14"], "supplier_id": "SUP-003",
            "shipment_id": "SHP-0003", "caused_by": "supplier_delay",
            "reason": "stockout_risk_pending_delayed_shipment",
            "refs": ["SL-100", "RP-200", "SP-410"],
            "summary": "SHP-0003 is 14 days late (expected 2015-12-05, actual 2015-12-19). On-hand "
                       "falls to 86 then 0, both below the 151-unit safety stock, right before TX_2's "
                       "Christmas-week demand. The delayed quantity is already in transit, so the "
                       "Replenishment agent raises no new order but flags expedite; the Planner "
                       "Copilot enforces the service-level (SL-100) gate with human review.",
        },
        raw_slice={"skus": ["HOUSEHOLD_1_447"], "stores": ["TX_2"],
                   "suppliers": ["SUP-003"], "shipments": ["SHP-0003"], "promo_events": [],
                   "sources": ["supplier_data", "inventory_snapshots", "pos_transactions"]},
        request_intent="assess_stockout_risk_from_supplier_delay",
        has_promo=False, anomaly=False, planner_hitl=True,
    ),
    # ------------------------------------------------------------------ IPF-004
    _make(
        "IPF-004", "partial_fill_stockout_reorder",
        "Partial-fill shipment drives a stockout — reorder approved within budget",
        "reorder_approved", False,
        anchor={
            "type": "stockout_risk", "sku": "HOBBIES_1_048", "stores": ["CA_1"],
            "weeks": ["2015-12-14"], "supplier_id": "SUP-005",
            "shipment_id": "SHP-0005", "caused_by": "supplier_fill_rate",
            "reason": "stockout_risk_after_fill_rate_shortfall",
            "refs": ["SL-100", "RP-200", "RP-210"],
            "summary": "SHP-0005 arrives on time but only 57.9% filled (139 of 240 units), below the "
                       "70% SP-400 threshold. On-hand falls to 28, below the 103-unit safety stock. The "
                       "Replenishment agent recommends a follow-up order of max(MOQ, shortfall); the "
                       "Planner Copilot approves it within budget — no human review.",
        },
        raw_slice={"skus": ["HOBBIES_1_048"], "stores": ["CA_1"],
                   "suppliers": ["SUP-005"], "shipments": ["SHP-0005"], "promo_events": [],
                   "sources": ["supplier_data", "inventory_snapshots", "pos_transactions"]},
        request_intent="assess_stockout_risk_from_partial_fill",
        has_promo=False, anomaly=False, planner_hitl=False,
    ),
    # ------------------------------------------------------------------ IPF-005
    _make(
        "IPF-005", "demand_anomaly_no_action",
        "Unexplained demand anomaly — flagged for review, no supply-side action",
        "flagged_anomaly_no_action", True,
        anchor={
            "type": "demand_anomaly", "sku": "HOBBIES_1_268", "stores": ["CA_1"],
            "weeks": ["2015-12-07"], "anomaly": True,
            "reason": "unexplained_demand_dip",
            "refs": ["SN-500"],
            "summary": "Units drop to 1, 1, 3 on 2015-12-09/10/11 with no promo flag, calendar event, "
                       "or supplier disruption attached. The Forecasting agent detects the statistical "
                       "anomaly and routes the short-term trend to human review; no supply-side order is "
                       "raised (demand-side noise, not a supply gap).",
        },
        raw_slice={"skus": ["HOBBIES_1_268"], "stores": ["CA_1"],
                   "suppliers": ["SUP-006"], "shipments": [], "promo_events": [],
                   "sources": ["pos_transactions", "inventory_snapshots"]},
        request_intent="investigate_demand_anomaly",
        has_promo=False, anomaly=True, planner_hitl=False,
    ),
]


CASE_FOLDERS: dict[str, str] = {
    "IPF-001": "case-01",
    "IPF-002": "case-02",
    "IPF-003": "case-03",
    "IPF-004": "case-04",
    "IPF-005": "case-05",
}


def scenario_folder(scenario: dict) -> str:
    """Long-form legacy scenario label, e.g. 'IPF-001_seasonal_happy_path'."""
    return f"{scenario['scenario_id']}_{scenario['path']}"


def case_folder(scenario: dict) -> str:
    """Demo folder under dataset-seed/cases/."""
    return CASE_FOLDERS[scenario["scenario_id"]]


def stage_folder(stage: dict) -> str:
    return STAGE_FOLDERS[stage["stage"]]


def scenario_folder_stage(name: str) -> str:
    return STAGE_FOLDERS[name]
