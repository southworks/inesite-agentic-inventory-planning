using System.Text.Json;

namespace CohereInventoryAndTrend.Mcp.Models;

public sealed class ReplenishmentPlanDraft
{
    public required string CaseId { get; init; }

    public required string ExecutionId { get; init; }

    public required string PlanStatus { get; init; }

    public required JsonElement SignalIngestionSummary { get; init; }

    public required JsonElement FeatureCausalitySummary { get; init; }

    public required JsonElement ForecastingSummary { get; init; }

    public required IReadOnlyList<string> OperationalRequirements { get; init; }
}

public sealed class BuildReplenishmentDraftResponse
{
    public required ReplenishmentPlanDraft Draft { get; init; }

    public required IReadOnlyList<string> MissingFields { get; init; }

    public required IReadOnlyList<string> BlockingIssues { get; init; }
}
