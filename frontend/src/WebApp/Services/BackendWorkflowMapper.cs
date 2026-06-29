using Grok.InventoryAndTrend.WebApp.Contracts;
using Grok.InventoryAndTrend.WebApp.Contracts.Api.Backend;
using Grok.InventoryAndTrend.WebApp.Models;

namespace Grok.InventoryAndTrend.WebApp.Services;

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

    public WorkflowProgressResponse MapBasicWorkflowStatus(
        BackendBasicWorkflowStatusResponse backend,
        string planId,
        HumanDecisionRecord? humanDecision = null)
    {
        var backendStatus = MapStatus(backend.Status);
        var stages = BuildStages(backend, backendStatus);
        var uiStatus = ResolveUiStatus(backendStatus, humanDecision, stages);
        var currentStage = ResolveCurrentStage(stages, uiStatus);

        return new WorkflowProgressResponse
        {
            PlanId = planId,
            ExecutionId = backend.ExecutionId,
            Status = uiStatus,
            CurrentStage = currentStage,
            StatusMessage = ResolveStatusMessage(backend, uiStatus, humanDecision),
            Stages = stages,
            HumanDecision = humanDecision
        };
    }

    private static WorkflowRunStatus ResolveUiStatus(
        WorkflowRunStatus backendStatus,
        HumanDecisionRecord? humanDecision,
        IReadOnlyList<WorkflowStageProgress> stages)
    {
        if (humanDecision is not null)
        {
            return humanDecision.Decision == HumanDecisionType.Reject
                ? WorkflowRunStatus.Failed
                : WorkflowRunStatus.Completed;
        }

        if (backendStatus is WorkflowRunStatus.Completed
            || (backendStatus is WorkflowRunStatus.Running && AllAgentStagesComplete(stages)))
        {
            return WorkflowRunStatus.AwaitingHumanApproval;
        }

        return backendStatus;
    }

    private static bool AllAgentStagesComplete(IReadOnlyList<WorkflowStageProgress> stages) =>
        stages.Count > 0 && stages.All(stage => stage.Status == "Completed");

    private static string ResolveStatusMessage(
        BackendBasicWorkflowStatusResponse backend,
        WorkflowRunStatus uiStatus,
        HumanDecisionRecord? humanDecision)
    {
        if (uiStatus is WorkflowRunStatus.Failed && !string.IsNullOrWhiteSpace(backend.FailureReason))
        {
            return backend.FailureReason;
        }

        if (uiStatus is WorkflowRunStatus.AwaitingHumanApproval)
        {
            return "Workflow complete — awaiting planner approval.";
        }

        if (humanDecision is not null && uiStatus is WorkflowRunStatus.Completed)
        {
            return $"Plan {humanDecision.Decision.ToString().ToLowerInvariant()} by reviewer.";
        }

        if (humanDecision is not null && uiStatus is WorkflowRunStatus.Failed)
        {
            return "Plan rejected by reviewer.";
        }

        return uiStatus switch
        {
            WorkflowRunStatus.Running => "Workflow in progress…",
            WorkflowRunStatus.Completed => "Workflow completed.",
            WorkflowRunStatus.Failed => backend.FailureReason ?? "Workflow failed.",
            _ => string.Empty
        };
    }

    private List<WorkflowStageProgress> BuildStages(
        BackendBasicWorkflowStatusResponse backend,
        WorkflowRunStatus backendStatus)
    {
        var outputs = backend.AgentOutputs;
        var stageOutputs = new Dictionary<WorkflowStageKey, string?>
        {
            [WorkflowStageKey.SignalIngestion] = outputs.SignalIngestion,
            [WorkflowStageKey.FeatureAndCausality] = outputs.FeatureCausality,
            [WorkflowStageKey.Forecasting] = outputs.Forecasting,
            [WorkflowStageKey.ReplenishmentAndAllocation] = outputs.ReplenishmentAllocation,
            [WorkflowStageKey.PlannerCopilot] = outputs.PlannerCopilot
        };

        var stages = new List<WorkflowStageProgress>();
        var runningStageAssigned = false;

        foreach (var stageKey in WorkflowStageUi.OrderedStages)
        {
            var rawOutput = stageOutputs.GetValueOrDefault(stageKey);
            var hasOutput = !string.IsNullOrWhiteSpace(rawOutput);
            string stageStatus;

            if (hasOutput)
            {
                stageStatus = "Completed";
            }
            else if (backendStatus is WorkflowRunStatus.Running && !runningStageAssigned)
            {
                stageStatus = "Running";
                runningStageAssigned = true;
            }
            else if (backendStatus is WorkflowRunStatus.Failed && !runningStageAssigned && stages.All(s => s.Status != "Running"))
            {
                stageStatus = "Failed";
            }
            else
            {
                stageStatus = "Pending";
            }

            AgentStageResult? parsedOutput = null;
            if (hasOutput)
            {
                parsedOutput = _parser.Parse(stageKey, rawOutput!);
            }

            stages.Add(new WorkflowStageProgress
            {
                StageKey = stageKey,
                Title = WorkflowStageUi.ToTitle(stageKey),
                Status = stageStatus,
                CompletedAt = hasOutput ? backend.LastUpdatedUtc : null,
                Output = parsedOutput
            });
        }

        return stages;
    }

    private static WorkflowStageKey? ResolveCurrentStage(
        IReadOnlyList<WorkflowStageProgress> stages,
        WorkflowRunStatus uiStatus)
    {
        if (uiStatus is WorkflowRunStatus.AwaitingHumanApproval)
        {
            return WorkflowStageKey.PlannerCopilot;
        }

        if (uiStatus is WorkflowRunStatus.Running)
        {
            return stages.FirstOrDefault(s => s.Status == "Running")?.StageKey
                   ?? stages.FirstOrDefault(s => s.Status == "Pending")?.StageKey;
        }

        if (uiStatus is WorkflowRunStatus.Completed or WorkflowRunStatus.Failed)
        {
            return WorkflowStageKey.PlannerCopilot;
        }

        return null;
    }
}
