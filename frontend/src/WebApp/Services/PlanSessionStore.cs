using Cohere.InventoryAndTrend.WebApp.Contracts;

namespace Cohere.InventoryAndTrend.WebApp.Services;

public sealed class PlanSession
{
    public string PlanId { get; set; } = string.Empty;

    public string ScenarioId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public PlanContext Context { get; set; } = new();

    public WorkflowRunStatus Status { get; set; } = WorkflowRunStatus.Pending;

    public string? ExecutionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public HumanDecisionRecord? HumanDecision { get; set; }
}

public sealed class PlanSessionStore
{
    private readonly Dictionary<string, PlanSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public PlanSession Open(PlanSession session)
    {
        _sessions[session.PlanId] = session;
        return session;
    }

    public PlanSession? Get(string planId) =>
        _sessions.TryGetValue(planId, out var session) ? session : null;

    public void Update(PlanSession session) => _sessions[session.PlanId] = session;

    public IReadOnlyList<PlanSummary> GetSummaries() =>
        _sessions.Values
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new PlanSummary
            {
                PlanId = s.PlanId,
                ScenarioId = s.ScenarioId,
                Title = s.Title,
                Status = s.Status,
                ExecutionId = s.ExecutionId,
                CreatedAt = s.CreatedAt
            })
            .ToList();
}
