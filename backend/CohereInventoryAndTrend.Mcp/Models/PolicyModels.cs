namespace CohereInventoryAndTrend.Mcp.Models;

public sealed class PolicyEntry
{
    public required string PolicyRef { get; init; }

    public required string Rule { get; init; }

    public required string Threshold { get; init; }

    public required string Action { get; init; }

    public required string Exception { get; init; }

    public required string FullText { get; init; }
}

public sealed class PolicyMatch
{
    public required string PolicyRef { get; init; }

    public required string Rule { get; init; }

    public required string Threshold { get; init; }

    public required string Action { get; init; }

    public required string Exception { get; init; }

    public double Score { get; init; }
}

public sealed class GetRelevantPoliciesResponse
{
    public required string Query { get; init; }

    public required IReadOnlyList<PolicyMatch> Policies { get; init; }
}
