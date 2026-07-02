using GrokInventoryAndTrend.WebApp.Contracts;

namespace GrokInventoryAndTrend.WebApp.Models;

public static class WorkflowApprovalReadiness
{
    public static bool IsReadyForHumanApproval(WorkflowProgressResponse? progress) =>
        IsReadyForHumanApproval(progress?.Stages);

    public static bool IsReadyForHumanApproval(IReadOnlyList<WorkflowStageProgress>? stages)
    {
        var plannerStage = stages?.FirstOrDefault(s => s.StageKey == WorkflowStageKey.PlannerCopilot);

        return plannerStage is { Status: "Completed", Output: PlannerCopilotStageResult copilot }
               && !string.IsNullOrWhiteSpace(copilot.Summary)
               && !string.IsNullOrWhiteSpace(copilot.ApprovalAssessment);
    }
}
