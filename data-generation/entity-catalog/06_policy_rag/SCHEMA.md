# 06 Policy RAG Schema

Plain-text retail planning policy documents, retrieved via Cohere Embed/Rerank by the
agents that need them (Replenishment & Allocation, Forecasting). Same document shape as
the FSI dataset-seed's `08_policy_rag/`, adapted to retail.

## Document format

```
Policy Ref: <PREFIX-NNN>
Rule: <one-line statement of what the policy governs>
Threshold: <the concrete numbers/formulas that make the rule checkable>
Action: <what an agent should do when the threshold is crossed>
Exception: <narrow carve-outs, if any>
```

A single `.txt` file may contain more than one `Policy Ref` block when the rules are
closely related (see `replenishment_policy.txt`, `supplier_performance_policy.txt`,
`seasonal_planning_policy.txt`).

## Ref code prefixes

| Prefix | File | Covers |
|---|---|---|
| `SL-1xx` | `service_level_policy.txt` | Fill rate target, days of cover, safety stock / target on-hand sizing |
| `RP-2xx` | `replenishment_policy.txt` | Reorder point, order quantity formula, MOQ enforcement |
| `BG-3xx` | `budget_allocation_policy.txt` | Per-order budget cap relative to average demand |
| `SP-4xx` | `supplier_performance_policy.txt` | Fill-rate and lead-time disruption thresholds |
| `SN-5xx` | `seasonal_planning_policy.txt` | Holiday-week and promotion-week forecasting rules |

## Notes

- All thresholds here are also the formulas implemented in `generate_normalized_layers.py`
  to compute `07_decision_ground_truth/` — the ground truth is calculable from
  `00_raw/` + these policy refs, not hand-asserted.
- `top_policy_refs` in `07_decision_ground_truth/*.json` cites these ref codes directly.
