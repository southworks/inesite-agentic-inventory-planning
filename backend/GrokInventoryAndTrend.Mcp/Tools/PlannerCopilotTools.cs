using System.ComponentModel;
using GrokInventoryAndTrend.Mcp.Adapters;
using GrokInventoryAndTrend.Mcp.Models;
using ModelContextProtocol.Server;

namespace GrokInventoryAndTrend.Mcp.Tools;

public sealed class PlannerCopilotTools
{
    private readonly LocalKnowledgeAdapter _localKnowledgeAdapter;

    public PlannerCopilotTools(LocalKnowledgeAdapter localKnowledgeAdapter)
    {
        _localKnowledgeAdapter = localKnowledgeAdapter;
    }

    [McpServerTool]
    [Description("Retrieves budget and service-level planning constraints from local demo knowledge files.")]
    public Task<GetRelevantKnowledgeResponse> GetPlanningConstraints(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        var query = DemoToolDefaults.CaseQuery(caseId, "Retrieve budget and service-level planning constraints");
        return _localKnowledgeAdapter.GetPlanningConstraintKnowledgeAsync(
            query,
            DemoToolDefaults.CaseContext(caseId, executionId),
            DemoToolDefaults.DefaultTopK,
            cancellationToken);
    }
}
