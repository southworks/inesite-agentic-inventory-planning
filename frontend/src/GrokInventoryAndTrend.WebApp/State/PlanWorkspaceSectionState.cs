namespace GrokInventoryAndTrend.WebApp.State;

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
        if (_expanded.Contains(sectionId))
        {
            _expanded.Remove(sectionId);
        }
        else
        {
            _expanded.Add(sectionId);
        }

        OnChange?.Invoke();
    }

    public void ExpandSection(string sectionId)
    {
        if (_expanded.Add(sectionId))
        {
            OnChange?.Invoke();
        }
    }

    /// <summary>
    /// Auto-expands a stage when it becomes active. Does not re-expand on subsequent polls.
    /// </summary>
    public void ExpandSectionForNewStage(string sectionId) => ExpandSection(sectionId);

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

    public void ResetSections()
    {
        if (_expanded.Count == 0)
        {
            return;
        }

        _expanded.Clear();
        OnChange?.Invoke();
    }
}
