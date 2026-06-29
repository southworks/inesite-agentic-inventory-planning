using System.ComponentModel;
using GrokInventoryAndTrend.Mcp.Adapters;
using GrokInventoryAndTrend.Mcp.Models;
using ModelContextProtocol.Server;

namespace GrokInventoryAndTrend.Mcp.Tools;

public sealed class PlannerCopilotTools
{
    private readonly PolicyIndexAdapter _policyIndexAdapter;

    public PlannerCopilotTools(PolicyIndexAdapter policyIndexAdapter)
    {
        _policyIndexAdapter = policyIndexAdapter;
    }

    [McpServerTool]
    [Description("Retrieves budget and service-level planning constraints from the seeded policy index.")]
    public async Task<GetRelevantKnowledgeResponse> GetPlanningConstraints(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        var query = DemoToolDefaults.CaseQuery(caseId, "Retrieve budget and service-level planning constraints");
        var policies = await _policyIndexAdapter.GetRelevantPoliciesAsync(
            query,
            DemoToolDefaults.CaseContext(caseId, executionId),
            DemoToolDefaults.DefaultTopK,
            cancellationToken);

        return ToKnowledgeResponse(policies);
    }

    [McpServerTool]
    [Description("Retrieves relevant inventory planning policy entries for budget, service-level, and replenishment review.")]
    public Task<GetRelevantPoliciesResponse> GetRelevantPolicies(
        string caseId,
        string executionId,
        [Description("Natural-language query describing the planning or policy topic to retrieve.")]
        string query,
        int topK = DemoToolDefaults.DefaultTopK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        return _policyIndexAdapter.GetRelevantPoliciesAsync(
            query,
            DemoToolDefaults.CaseContext(caseId, executionId),
            topK,
            cancellationToken);
    }

    [McpServerTool]
    [Description("Retrieves policy entries by exact policy reference codes, such as SL-100 or BG-300.")]
    public Task<GetRelevantPoliciesResponse> GetPoliciesByRefs(
        [Description("Policy reference codes to retrieve, for example SL-100 or RP-200.")]
        string[] policyRefs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policyRefs);
        return _policyIndexAdapter.GetPoliciesByRefsAsync(policyRefs, cancellationToken);
    }

    private static GetRelevantKnowledgeResponse ToKnowledgeResponse(GetRelevantPoliciesResponse policies) =>
        new()
        {
            Query = policies.Query,
            Entries = policies.Policies
                .Select(policy => new KnowledgeMatch
                {
                    KnowledgeRef = policy.PolicyRef,
                    Rule = policy.Rule,
                    Threshold = policy.Threshold,
                    Action = policy.Action,
                    Exception = policy.Exception,
                    Score = policy.Score
                })
                .ToArray()
        };
}
