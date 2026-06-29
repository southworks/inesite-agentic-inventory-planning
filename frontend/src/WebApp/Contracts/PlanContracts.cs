namespace Grok.InventoryAndTrend.WebApp.Contracts;

public enum WorkflowRunStatus
{
    Pending,
    Running,
    AwaitingHumanApproval,
    Completed,
    Failed
}

public enum WorkflowStageKey
{
    SignalIngestion,
    FeatureAndCausality,
    Forecasting,
    ReplenishmentAndAllocation,
    PlannerCopilot
}

public enum ScenarioOutcomeTag
{
    HealthyRun,
    AnomaliesExpected,
    BudgetPressure,
    ServiceLevelRisk
}

public enum HumanDecisionType
{
    Approve,
    ApproveWithAdjustments,
    Reject
}

public sealed class PlanSummary
{
    public string PlanId { get; set; } = string.Empty;

    public string ScenarioId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public WorkflowRunStatus Status { get; set; }

    public string? ExecutionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PlanContext
{
    public string Category { get; set; } = string.Empty;

    public string Campaign { get; set; } = string.Empty;

    public string PlanningHorizon { get; set; } = string.Empty;

    public decimal BudgetCap { get; set; }

    public decimal TargetFillRate { get; set; }

    public int AffectedSkuCount { get; set; }

    public IReadOnlyList<string> SignalSources { get; set; } = [];
}

public sealed class PlanDetailResponse
{
    public string PlanId { get; set; } = string.Empty;

    public string ScenarioId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public PlanContext Context { get; set; } = new();

    public WorkflowRunStatus Status { get; set; }

    public string? ExecutionId { get; set; }

    public IReadOnlyList<string> AllowedActions { get; set; } = [];
}

public sealed class WorkflowStageProgress
{
    public WorkflowStageKey StageKey { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Status { get; set; } = "Pending";

    public DateTimeOffset? CompletedAt { get; set; }

    public AgentStageResult? Output { get; set; }
}

public sealed class WorkflowProgressResponse
{
    public string PlanId { get; set; } = string.Empty;

    public string ExecutionId { get; set; } = string.Empty;

    public WorkflowRunStatus Status { get; set; }

    public WorkflowStageKey? CurrentStage { get; set; }

    public string StatusMessage { get; set; } = string.Empty;

    public IReadOnlyList<WorkflowStageProgress> Stages { get; set; } = [];

    public HumanDecisionRecord? HumanDecision { get; set; }
}

public sealed class HumanDecisionRecord
{
    public HumanDecisionType Decision { get; set; }

    public string Notes { get; set; } = string.Empty;

    public DateTimeOffset SubmittedAt { get; set; }
}

public sealed class StartWorkflowResponse
{
    public string ExecutionId { get; set; } = string.Empty;

    public WorkflowRunStatus Status { get; set; }
}

public sealed class SubmitHumanDecisionRequest
{
    public HumanDecisionType Decision { get; set; }

    public string Notes { get; set; } = string.Empty;
}
