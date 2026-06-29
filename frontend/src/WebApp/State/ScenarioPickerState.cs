using Grok.InventoryAndTrend.WebApp.Contracts;
using Grok.InventoryAndTrend.WebApp.Services;

namespace Grok.InventoryAndTrend.WebApp.State;

public sealed class ScenarioPickerFilter
{
    public static bool Matches(SeedPlanDefinitionDto scenario, string? searchQuery, ScenarioOutcomeTag? outcomeFilter)
    {
        if (outcomeFilter.HasValue && scenario.OutcomeTag != outcomeFilter.Value)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return true;
        }

        var query = searchQuery.Trim();
        return scenario.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
               || scenario.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
               || scenario.Context.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
               || scenario.Context.Campaign.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ScenarioPickerState
{
    private readonly IPlanningApiClient _client;

    public ScenarioPickerState(IPlanningApiClient client)
    {
        _client = client;
    }

    public IReadOnlyList<SeedPlanDefinitionDto> Scenarios { get; private set; } = [];

    public string SearchQuery { get; private set; } = string.Empty;

    public ScenarioOutcomeTag? OutcomeFilter { get; private set; }

    public SeedPlanDefinitionDto? SelectedScenario { get; private set; }

    public bool IsLoading { get; private set; }

    public string? Error { get; private set; }

    public event Action? OnChange;

    public IEnumerable<SeedPlanDefinitionDto> FilteredScenarios =>
        Scenarios.Where(s => ScenarioPickerFilter.Matches(s, SearchQuery, OutcomeFilter));

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        Error = null;
        Notify();

        try
        {
            Scenarios = await _client.GetScenariosAsync(cancellationToken);
            SelectedScenario ??= Scenarios.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsLoading = false;
            Notify();
        }
    }

    public void SetSearchQuery(string value)
    {
        SearchQuery = value;
        Notify();
    }

    public void SetOutcomeFilter(ScenarioOutcomeTag? value)
    {
        OutcomeFilter = OutcomeFilter == value ? null : value;
        Notify();
    }

    public void SelectScenario(string scenarioId)
    {
        SelectedScenario = Scenarios.FirstOrDefault(s =>
            string.Equals(s.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
        Notify();
    }

    private void Notify() => OnChange?.Invoke();
}
