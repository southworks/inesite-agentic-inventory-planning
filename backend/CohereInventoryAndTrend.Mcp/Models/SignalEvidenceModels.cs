namespace CohereInventoryAndTrend.Mcp.Models;

public sealed class SignalMatch
{
    public required string DocumentId { get; init; }

    public required string DocumentType { get; init; }

    public required string Category { get; init; }

    public required string Snippet { get; init; }

    public double Score { get; init; }
}

public sealed class SearchSignalEvidenceResponse
{
    public required string CaseId { get; init; }

    public required string ExecutionId { get; init; }

    public required string Query { get; init; }

    public required IReadOnlyList<SignalMatch> Matches { get; init; }
}

public sealed class DriverCategoryContext
{
    public required string Category { get; init; }

    public required IReadOnlyList<SignalMatch> Matches { get; init; }
}

public sealed class GetDriverContextResponse
{
    public required string CaseId { get; init; }

    public required string ExecutionId { get; init; }

    public required IReadOnlyList<DriverCategoryContext> Categories { get; init; }
}

public sealed class GetForecastingContextResponse
{
    public required string CaseId { get; init; }

    public required string ExecutionId { get; init; }

    public required IReadOnlyList<DriverCategoryContext> Categories { get; init; }
}
