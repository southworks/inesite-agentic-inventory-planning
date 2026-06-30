using GrokInventoryAndTrend.WebApp.Contracts;

namespace GrokInventoryAndTrend.WebApp.Services;

public interface IPlanningApiClient
{
    Task<IReadOnlyList<SeedPlanDefinitionDto>> GetScenariosAsync(CancellationToken cancellationToken = default);

    Task<PlanDetailResponse> CreatePlanAsync(string scenarioId, CancellationToken cancellationToken = default);

    Task<PlanDetailResponse?> GetPlanAsync(
        string planId,
        string? executionId = null,
        CancellationToken cancellationToken = default);

    Task<StartWorkflowResponse> StartWorkflowAsync(string planId, CancellationToken cancellationToken = default);

    Task<WorkflowProgressResponse> GetWorkflowStatusAsync(
        string executionId,
        string? planId = null,
        CancellationToken cancellationToken = default);

    Task<WorkflowProgressResponse> SubmitHumanDecisionAsync(
        string planId,
        string executionId,
        SubmitHumanDecisionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class SeedPlanDefinitionDto
{
    public string ScenarioId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public ScenarioOutcomeTag OutcomeTag { get; set; }

    public PlanContext Context { get; set; } = new();
}
