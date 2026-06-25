namespace InventoryPlanning.Api.Host.Contracts;

public sealed class BasicWorkflowStatusResponse
{
    public required string ExecutionId { get; init; }

    public required string CaseId { get; init; }

    public required string Status { get; init; }

    public required BasicWorkflowAgentOutputsResponse AgentOutputs { get; init; }

    public string? FailureReason { get; init; }

    public required DateTimeOffset LastUpdatedUtc { get; init; }
}

public sealed class BasicWorkflowApprovalRequest
{
    public required bool Approved { get; init; }

    public string? ReviewerComment { get; init; }
}

public sealed class BasicWorkflowAgentOutputsResponse
{
    public string? DocumentProcessing { get; init; }

    public string? PlanningReview { get; init; }

    public string? ResponsibleAi { get; init; }

    public string? PlanExecution { get; init; }
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

    public required string BlobName { get; init; }

    public required string Reference { get; init; }

    public required DateTimeOffset LastModifiedUtc { get; init; }
}

public sealed class ProblemDetailsResponse
{
    public required string Title { get; init; }

    public required string Detail { get; init; }
}
