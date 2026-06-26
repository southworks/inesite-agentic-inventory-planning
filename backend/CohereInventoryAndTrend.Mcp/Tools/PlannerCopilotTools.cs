using System.ComponentModel;
using CohereInventoryAndTrend.Mcp.Adapters;
using CohereInventoryAndTrend.Mcp.Models;
using ModelContextProtocol.Server;

namespace CohereInventoryAndTrend.Mcp.Tools;

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
        return _localKnowledgeAdapter.GetRelevantKnowledgeAsync(
            query,
            DemoToolDefaults.CaseContext(caseId, executionId),
            DemoToolDefaults.DefaultTopK,
            cancellationToken);
    }
}
