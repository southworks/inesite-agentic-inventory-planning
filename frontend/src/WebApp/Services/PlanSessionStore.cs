using Grok.InventoryAndTrend.WebApp.Contracts;
using Grok.InventoryAndTrend.WebApp.Contracts.Api.Backend;

namespace Grok.InventoryAndTrend.WebApp.Services;

public sealed class PlanSession
{
    public string PlanId { get; set; } = string.Empty;

    public string ScenarioId { get; set; } = string.Empty;

    public string CaseId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public PlanContext Context { get; set; } = new();

    public WorkflowRunStatus Status { get; set; } = WorkflowRunStatus.Pending;

    public string? ExecutionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public HumanDecisionRecord? HumanDecision { get; set; }

    public BackendBasicWorkflowStatusResponse? LastBackendStatus { get; set; }
}

public sealed class PlanSessionStore
{
    private readonly Dictionary<string, PlanSession> _activeByPlanId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PlanSession> _executionsById = new(StringComparer.OrdinalIgnoreCase);

    public PlanSession? Get(string planId) =>
        _activeByPlanId.TryGetValue(planId, out var session) ? session : null;

    public PlanSession? GetByExecutionId(string executionId)
    {
        if (_executionsById.TryGetValue(executionId, out var archived))
        {
            return archived;
        }

        return _activeByPlanId.Values.FirstOrDefault(s =>
            string.Equals(s.ExecutionId, executionId, StringComparison.OrdinalIgnoreCase));
    }

    public void PrepareActiveSlot(string planId, PlanSession session)
    {
        ArchiveActiveExecutionIfNeeded(planId);
        _activeByPlanId[planId] = session;
    }

    public void RegisterExecution(PlanSession session)
    {
        if (string.IsNullOrWhiteSpace(session.ExecutionId))
        {
            return;
        }

        _executionsById[session.ExecutionId] = session;
    }

    public void Update(PlanSession session)
    {
        if (!string.IsNullOrWhiteSpace(session.ExecutionId))
        {
            _executionsById[session.ExecutionId] = session;
        }

        if (_activeByPlanId.TryGetValue(session.PlanId, out var active)
            && (ReferenceEquals(active, session)
                || (!string.IsNullOrWhiteSpace(session.ExecutionId)
                    && string.Equals(active.ExecutionId, session.ExecutionId, StringComparison.OrdinalIgnoreCase))))
        {
            _activeByPlanId[session.PlanId] = session;
        }
    }

    public IReadOnlyList<PlanSummary> GetSummaries() =>
        _executionsById.Values
            .OrderByDescending(s => s.CreatedAt)
            .Select(ToSummary)
            .ToList();

    private void ArchiveActiveExecutionIfNeeded(string planId)
    {
        if (!_activeByPlanId.TryGetValue(planId, out var existing)
            || string.IsNullOrWhiteSpace(existing.ExecutionId))
        {
            return;
        }

        if (!_executionsById.ContainsKey(existing.ExecutionId))
        {
            _executionsById[existing.ExecutionId] = CloneSession(existing);
        }
    }

    private static PlanSession CloneSession(PlanSession source) =>
        new()
        {
            PlanId = source.PlanId,
            ScenarioId = source.ScenarioId,
            CaseId = source.CaseId,
            Title = source.Title,
            Description = source.Description,
            Context = source.Context,
            Status = source.Status,
            ExecutionId = source.ExecutionId,
            CreatedAt = source.CreatedAt,
            HumanDecision = source.HumanDecision,
            LastBackendStatus = source.LastBackendStatus
        };

    private static PlanSummary ToSummary(PlanSession session) =>
        new()
        {
            PlanId = session.PlanId,
            ScenarioId = session.ScenarioId,
            Title = session.Title,
            Status = session.Status,
            ExecutionId = session.ExecutionId,
            CreatedAt = session.CreatedAt
        };
}
