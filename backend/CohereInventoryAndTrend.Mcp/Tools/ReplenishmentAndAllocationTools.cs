using System.ComponentModel;
using CohereInventoryAndTrend.Mcp.Adapters;
using CohereInventoryAndTrend.Mcp.Builders;
using CohereInventoryAndTrend.Mcp.Models;
using ModelContextProtocol.Server;

namespace CohereInventoryAndTrend.Mcp.Tools;

public sealed class ReplenishmentAndAllocationTools
{
    private readonly PlanningDataAdapter _planningDataAdapter;
    private readonly ReplenishmentPlanBuilder _replenishmentPlanBuilder;

    public ReplenishmentAndAllocationTools(
        PlanningDataAdapter planningDataAdapter,
        ReplenishmentPlanBuilder replenishmentPlanBuilder)
    {
        _planningDataAdapter = planningDataAdapter;
        _replenishmentPlanBuilder = replenishmentPlanBuilder;
    }

    [McpServerTool]
    [Description("Reads current inventory and supplier signals for replenishment and allocation decisions.")]
    public Task<GetPlanningSignalsResponse> GetReplenishmentSignals(
        string caseId,
        string executionId,
        CancellationToken cancellationToken)
        => _planningDataAdapter.GetPlanningSignalsAsync(caseId, executionId, cancellationToken);

    [McpServerTool]
    [Description("Builds deterministic draft PO/TO recommendations for the demo planning case.")]
    public BuildReplenishmentDraftResponse BuildReplenishmentRecommendations(
        string caseId,
        string executionId)
        => _replenishmentPlanBuilder.Build(caseId, executionId);
}
