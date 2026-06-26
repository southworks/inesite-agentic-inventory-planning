using Cohere.InventoryAndTrend.WebApp.Contracts;
using Cohere.InventoryAndTrend.WebApp.Services;

namespace Cohere.InventoryAndTrend.WebApp.State;

public sealed class RecentPlansListState
{
    private readonly PlanSessionStore _sessions;

    public RecentPlansListState(PlanSessionStore sessions)
    {
        _sessions = sessions;
    }

    public IReadOnlyList<PlanSummary> Plans { get; private set; } = [];

    public event Action? OnChange;

    public void Refresh()
    {
        Plans = _sessions.GetSummaries();
        OnChange?.Invoke();
    }
}
