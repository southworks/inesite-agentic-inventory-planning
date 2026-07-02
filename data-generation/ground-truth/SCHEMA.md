# Ground Truth Schema

Ground-truth files are optional validation answer keys for the five e2e inventory-planning scenarios.

Scenario definitions live in `data-generation/scripts/scenarios.py`. Runtime demo inputs live under `dataset-seed/cases/`.

## Required top-level fields

- `scenario_id`
- `scenario_type`
- `path`
- `title`
- `scenario_folder`
- `final_outcome`
- `required_human_review`
- `primary_reason`
- `top_policy_refs`
- `summary_explanation`
- `stages`

## Stage fields

Each item in `stages[]` includes:

- `order`
- `stage`
- `agent`
- `agent_input`
- `gate`
- `policy_refs`
- `decision`
- `expected_output`

Stages that read case-scoped data may include `runtime_data_folder`, pointing at `dataset-seed/cases/{caseId}/fabric-pre-requisite-data/`.

## Runtime note

The application does not read these answer keys at runtime. New scenarios must be regenerated into `dataset-seed/`, added to API-supported case ids when needed, rebuilt into images or deployment packages, and redeployed.
