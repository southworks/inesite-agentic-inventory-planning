namespace GrokInventoryAndTrend.Api.Contracts;

public sealed class BasicWorkflowStatusResponse
{
    public required string ExecutionId { get; init; }

    public required string CaseId { get; init; }

    public required string Status { get; init; }

    public required BasicWorkflowAgentOutputsResponse AgentOutputs { get; init; }

    public string? FailureReason { get; init; }

    public required DateTimeOffset LastUpdatedUtc { get; init; }
}

public sealed class BasicWorkflowAgentOutputsResponse
{
    public string? SignalIngestion { get; init; }

    public string? FeatureCausality { get; init; }

    public string? Forecasting { get; init; }

    public string? ReplenishmentAllocation { get; init; }

    public string? PlannerCopilot { get; init; }
}

public sealed class CaseDocumentsResponse
{
    public required string CaseId { get; init; }

    public required IReadOnlyList<CaseDocumentResponse> Documents { get; init; }
}

public sealed class CaseDocumentResponse
{
    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required string DocumentPath { get; init; }

    public required string Reference { get; init; }

    public required DateTimeOffset LastModifiedUtc { get; init; }
}

public sealed class ProblemDetailsResponse
{
    public required string Title { get; init; }

    public required string Detail { get; init; }
}
