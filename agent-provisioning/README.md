# Agent Provisioning

This project provisions the four Azure AI Foundry prompt agents required by the loan and mortgage demo API.

In the standard Azure deployment flow, provisioning runs automatically as a Container Apps Job after infrastructure and MCP wiring complete. You do not need to run this CLI manually after clicking **Deploy to Azure**.

## Agents

| Agent | Responsibility | MCP path |
| --- | --- | --- |
| `document-processing-agent` | Extract and validate document evidence | `/document-retrieval/mcp` |
| `underwriting-agent` | Evaluate risk and produce underwriting recommendation | `/underwriting-rules/mcp` |
| `responsible-ai-agent` | Review fairness, policy, and governance concerns | `/policy-knowledge/mcp` |
| `loan-setup-agent` | Consolidate prior outputs and produce setup readiness | `/loan-setup/mcp` |

All agents use the same Foundry model deployment: **Cohere Command A**.

## Azure Deployment Lifecycle

During `Deploy to Azure`, [infra/main.bicep](../infra/main.bicep):

1. Provisions Foundry, Storage, Search, and model deployments.
2. Deploys the MCP Container App.
3. Starts the policy seed job.
4. Starts the agent provisioning Container Apps Job.
5. Waits for the job to finish before completing the deployment.

The provisioning container image is built from [Dockerfile](Dockerfile) and runs:

- prompt agent create/update with direct public MCP tools
- strict JSON schema output configuration
- idempotent version creation based on definition fingerprints

## Agent-as-Code Layout

```text
agents/
  document-processing-agent/
    agent.json
    instructions.md
    mcp.json
shared/
  agent-structured-output.schema.json
  underwriting-structured-output.schema.json
config/
  provisioning.json
```

## Configuration

[config/provisioning.json](config/provisioning.json) is the base configuration file:

```json
{
  "ProjectEndpoint": "",
  "ModelDeploymentName": "cohere-command-a",
  "McpBaseUrl": ""
}
```

Environment variable overrides:

- `AZURE_FOUNDRY_PROJECT_ENDPOINT` or `FOUNDRY_PROJECT_ENDPOINT`
- `AZURE_AI_MODEL_DEPLOYMENT_NAME` or `ModelDeploymentName`
- `MCP_BASE_URL`

## Optional Local Maintenance

Use this only when updating agent definitions outside the Azure deployment flow:

```powershell
./agent-provisioning/scripts/provision-agents.ps1 -ConfigPath agent-provisioning/config/provisioning.local.json
```

Or:

```powershell
dotnet run --project agent-provisioning/src/CohereLoanAndMortgage.AgentProvisioning -- `
  --config agent-provisioning/config/provisioning.local.json `
  --agents agent-provisioning/agents
```

## Idempotency and Fail-Fast Behavior

For each agent the CLI:

1. Loads manifest assets.
2. Validates required model deployment and MCP base URL configuration.
3. Reads the latest Foundry agent version when present.
4. Compares a deterministic definition fingerprint.
5. Creates a new version only when the definition changed.

Results are reported as `Created`, `Updated`, `Unchanged`, or `Failed`.

## Structured Output Contract

Each agent returns JSON with:

- `summary`
- `decision`
- `evidence`

The shared schema lives in [shared/agent-structured-output.schema.json](shared/agent-structured-output.schema.json).

`underwriting-agent` uses an extended strict schema in [shared/underwriting-structured-output.schema.json](shared/underwriting-structured-output.schema.json) that also requires:

- `riskLevel`
- `policyRefs`
- `anomalies`
- `keyFacts`

`responsible-ai-agent` uses [shared/responsible-ai-structured-output.schema.json](shared/responsible-ai-structured-output.schema.json) with:

- `approvalAssessment`
- `biasRisk`
- `supportingFacts`
- `concerns`
- `recommendations`

## Responsible AI demo scenarios

After HITL resume, the workflow sends underwriting plus human decision context to `responsible-ai-agent`. Expected outcomes:

| Underwriting | Human | Reviewer comment | Expected direction |
| --- | --- | --- | --- |
| Approve | approved | optional | `Approval Supported`, `biasRisk=None` |
| Reject | approved | missing | `Approval Not Supported`, `biasRisk=Potential` |
| Reject | approved | plausible rationale | `Approval Supported with Caveats` or `Partially Supported` |
| Approve | denied | optional | explain clearly; lower priority use case |

Resume the basic workflow with:

```json
{ "approved": true, "reviewerComment": "Override justified by compensating factors." }
```
