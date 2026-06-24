namespace Cohere.InventoryAndTrend.WebApp.State;

public sealed class PlanWorkspaceSectionState
{
    public const string Overview = "overview";
    public const string Workflow = "workflow";
    public const string StageSignalIngestion = "stage-signal-ingestion";
    public const string StageFeatureCausality = "stage-feature-causality";
    public const string StageForecasting = "stage-forecasting";
    public const string StageReplenishment = "stage-replenishment";
    public const string StagePlannerReview = "stage-planner-review";
    public const string Governance = "governance";

    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);

    private string? _currentPlanId;

    public event Action? OnChange;

    public bool IsExpanded(string sectionId) => _expanded.Contains(sectionId);

    public void Toggle(string sectionId)
    {
        if (!_expanded.Add(sectionId))
        {
            _expanded.Remove(sectionId);
        }

        OnChange?.Invoke();
    }

    public void ResetForPlan(string planId)
    {
        if (string.Equals(_currentPlanId, planId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentPlanId = planId;
        _expanded.Clear();
        OnChange?.Invoke();
    }
}
