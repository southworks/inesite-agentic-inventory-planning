namespace CohereInventoryAndTrend.Mcp.Models;

public sealed class PlanningProfile
{
    public string? Category { get; init; }

    public string? Campaign { get; init; }

    public string? PlanningHorizon { get; init; }

    public string? Locations { get; init; }

    public string? ProductScope { get; init; }

    public decimal? BudgetLimit { get; init; }

    public decimal? TargetServiceLevel { get; init; }
}

public sealed class GetPlanningProfileResponse
{
    public required string CaseId { get; init; }

    public required string ExecutionId { get; init; }

    public required PlanningProfile Profile { get; init; }

    public string? SourceDocumentId { get; init; }

    public bool Found { get; init; }
}
