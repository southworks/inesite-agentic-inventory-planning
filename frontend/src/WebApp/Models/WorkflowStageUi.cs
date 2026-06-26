using Cohere.InventoryAndTrend.WebApp.Contracts;

namespace Cohere.InventoryAndTrend.WebApp.Models;

public static class WorkflowStageUi
{
    public static readonly IReadOnlyList<WorkflowStageKey> OrderedStages =
    [
        WorkflowStageKey.SignalIngestion,
        WorkflowStageKey.FeatureAndCausality,
        WorkflowStageKey.Forecasting,
        WorkflowStageKey.ReplenishmentAndAllocation,
        WorkflowStageKey.PlannerCopilot
    ];

    public static string ToTitle(WorkflowStageKey stage) => stage switch
    {
        WorkflowStageKey.SignalIngestion => "Signal Ingestion",
        WorkflowStageKey.FeatureAndCausality => "Feature & Causality",
        WorkflowStageKey.Forecasting => "Demand Forecast",
        WorkflowStageKey.ReplenishmentAndAllocation => "Replenishment & Allocation",
        WorkflowStageKey.PlannerCopilot => "Planner Review",
        _ => stage.ToString()
    };

    public static string ToSectionId(WorkflowStageKey stage) => stage switch
    {
        WorkflowStageKey.SignalIngestion => "stage-signal-ingestion",
        WorkflowStageKey.FeatureAndCausality => "stage-feature-causality",
        WorkflowStageKey.Forecasting => "stage-forecasting",
        WorkflowStageKey.ReplenishmentAndAllocation => "stage-replenishment",
        WorkflowStageKey.PlannerCopilot => "stage-planner-review",
        _ => "stage-unknown"
    };

    public static string ToBusinessStatusLabel(WorkflowRunStatus status) => status switch
    {
        WorkflowRunStatus.Pending => "Not started",
        WorkflowRunStatus.Running => "In progress",
        WorkflowRunStatus.AwaitingHumanApproval => "Awaiting approval",
        WorkflowRunStatus.Completed => "Completed",
        WorkflowRunStatus.Failed => "Failed",
        _ => status.ToString()
    };

    public static string ToStatusBadgeClass(WorkflowRunStatus status) => status switch
    {
        WorkflowRunStatus.Completed => "status-badge--success",
        WorkflowRunStatus.Failed => "status-badge--danger",
        WorkflowRunStatus.AwaitingHumanApproval => "status-badge--warning",
        WorkflowRunStatus.Running => "status-badge--info",
        _ => "status-badge--neutral"
    };

    public static string ToOutcomeTagLabel(ScenarioOutcomeTag tag) => tag switch
    {
        ScenarioOutcomeTag.HealthyRun => "Healthy run",
        ScenarioOutcomeTag.AnomaliesExpected => "Anomalies expected",
        ScenarioOutcomeTag.BudgetPressure => "Budget pressure",
        ScenarioOutcomeTag.ServiceLevelRisk => "Service level risk",
        _ => tag.ToString()
    };

    public static string ToOutcomeTagClass(ScenarioOutcomeTag tag) => tag switch
    {
        ScenarioOutcomeTag.HealthyRun => "filter-chip--success",
        ScenarioOutcomeTag.AnomaliesExpected => "filter-chip--warning",
        ScenarioOutcomeTag.BudgetPressure => "filter-chip--danger",
        ScenarioOutcomeTag.ServiceLevelRisk => "filter-chip--info",
        _ => string.Empty
    };

    public static WorkflowStageKey? ParseStageKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.ToLowerInvariant() switch
        {
            "signalingestion" or "signal_ingestion" or "signal-ingestion" => WorkflowStageKey.SignalIngestion,
            "featureandcausality" or "feature_and_causality" or "feature-and-causality" => WorkflowStageKey.FeatureAndCausality,
            "forecasting" => WorkflowStageKey.Forecasting,
            "replenishmentandallocation" or "replenishment_and_allocation" or "replenishment-and-allocation" => WorkflowStageKey.ReplenishmentAndAllocation,
            "plannercopilot" or "planner_copilot" or "planner-copilot" => WorkflowStageKey.PlannerCopilot,
            _ => Enum.TryParse<WorkflowStageKey>(value, true, out var parsed) ? parsed : null
        };
    }
}
