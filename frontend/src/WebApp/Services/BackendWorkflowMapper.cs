using Cohere.InventoryAndTrend.WebApp.Contracts;
using Cohere.InventoryAndTrend.WebApp.Contracts.Api.Backend;
using Cohere.InventoryAndTrend.WebApp.Models;

namespace Cohere.InventoryAndTrend.WebApp.Services;

public sealed class BackendWorkflowMapper
{
    private readonly AgentOutputParser _parser;

    public BackendWorkflowMapper(AgentOutputParser parser)
    {
        _parser = parser;
    }

    public WorkflowRunStatus MapStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "pending" => WorkflowRunStatus.Pending,
        "running" or "inprogress" or "in_progress" => WorkflowRunStatus.Running,
        "awaitinghumanapproval" or "awaiting_human_approval" or "waitingforapproval" => WorkflowRunStatus.AwaitingHumanApproval,
        "completed" or "succeeded" => WorkflowRunStatus.Completed,
        "failed" => WorkflowRunStatus.Failed,
        _ => WorkflowRunStatus.Pending
    };

    public WorkflowProgressResponse MapStatusResponse(BackendWorkflowStatusResponse backend)
    {
        var stages = backend.Stages
            .Select(MapStage)
            .ToList();

        return new WorkflowProgressResponse
        {
            PlanId = backend.PlanId,
            ExecutionId = backend.ExecutionId,
            Status = MapStatus(backend.Status),
            CurrentStage = WorkflowStageUi.ParseStageKey(backend.CurrentStage),
            StatusMessage = backend.StatusMessage,
            Stages = stages
        };
    }

    private WorkflowStageProgress MapStage(BackendStageStatus backendStage)
    {
        var stageKey = WorkflowStageUi.ParseStageKey(backendStage.StageKey)
                       ?? WorkflowStageKey.SignalIngestion;

        AgentStageResult? output = null;
        if (!string.IsNullOrWhiteSpace(backendStage.OutputJson))
        {
            output = _parser.Parse(stageKey, backendStage.OutputJson);
        }

        return new WorkflowStageProgress
        {
            StageKey = stageKey,
            Title = WorkflowStageUi.ToTitle(stageKey),
            Status = backendStage.Status,
            CompletedAt = backendStage.CompletedAt,
            Output = output
        };
    }
}
