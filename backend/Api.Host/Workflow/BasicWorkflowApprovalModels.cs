namespace InventoryPlanning.Api.Host.Workflow;

public sealed class BasicWorkflowApprovalPrompt
{
    public required string CaseId { get; init; }

    public required string ExecutionId { get; init; }

    public required string Summary { get; init; }

    public required string PlanningReviewOutput { get; init; }
}

public sealed class BasicWorkflowApprovalDecision
{
    public required bool Approved { get; init; }

    public string? ReviewerComment { get; init; }
}
