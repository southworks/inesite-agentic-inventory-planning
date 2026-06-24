using Cohere.InventoryAndTrend.WebApp.Contracts;

namespace Cohere.InventoryAndTrend.WebApp.Services;

public sealed class LocalPlanningApiClient : IPlanningApiClient
{
    private readonly DatasetSeedCatalogService _catalog;
    private readonly PlanSessionStore _sessions;
    private readonly LocalPlanningSimulator _simulator;

    public LocalPlanningApiClient(
        DatasetSeedCatalogService catalog,
        PlanSessionStore sessions,
        LocalPlanningSimulator simulator)
    {
        _catalog = catalog;
        _sessions = sessions;
        _simulator = simulator;
    }

    public Task<IReadOnlyList<SeedPlanDefinitionDto>> GetScenariosAsync(CancellationToken cancellationToken = default)
    {
        var scenarios = _catalog.GetAll()
            .Select(s => new SeedPlanDefinitionDto
            {
                ScenarioId = s.ScenarioId,
                Title = s.Title,
                Description = s.Description,
                OutcomeTag = s.OutcomeTag,
                Context = s.Context
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<SeedPlanDefinitionDto>>(scenarios);
    }

    public Task<PlanDetailResponse> CreatePlanAsync(string scenarioId, CancellationToken cancellationToken = default)
    {
        var scenario = _catalog.GetById(scenarioId)
                       ?? throw new InvalidOperationException($"Scenario '{scenarioId}' not found.");

        var planId = Guid.NewGuid().ToString("N")[..12];
        var session = new PlanSession
        {
            PlanId = planId,
            ScenarioId = scenario.ScenarioId,
            Title = scenario.Title,
            Description = scenario.Description,
            Context = scenario.Context,
            Status = WorkflowRunStatus.Pending
        };

        _sessions.Open(session);
        return Task.FromResult(ToDetail(session));
    }

    public Task<PlanDetailResponse?> GetPlanAsync(string planId, CancellationToken cancellationToken = default)
    {
        var session = _sessions.Get(planId);
        return Task.FromResult(session is null ? null : ToDetail(session));
    }

    public Task<StartWorkflowResponse> StartWorkflowAsync(string planId, CancellationToken cancellationToken = default)
    {
        var session = _sessions.Get(planId)
                      ?? throw new InvalidOperationException($"Plan '{planId}' not found.");

        var scenario = _catalog.GetById(session.ScenarioId)
                       ?? throw new InvalidOperationException($"Scenario '{session.ScenarioId}' not found.");

        var run = _simulator.Start(
            planId,
            scenario,
            stage => _catalog.ReadStageOutputJson(scenario, stage));

        session.ExecutionId = run.ExecutionId;
        session.Status = WorkflowRunStatus.Running;
        _sessions.Update(session);

        return Task.FromResult(new StartWorkflowResponse
        {
            ExecutionId = run.ExecutionId,
            Status = WorkflowRunStatus.Running
        });
    }

    public Task<WorkflowProgressResponse> GetWorkflowStatusAsync(string executionId, CancellationToken cancellationToken = default)
    {
        var progress = _simulator.AdvanceAndBuildStatus(executionId);

        var session = _sessions.Get(progress.PlanId);
        if (session is not null)
        {
            session.Status = progress.Status;
            session.ExecutionId = progress.ExecutionId;
            session.HumanDecision = progress.HumanDecision;
            _sessions.Update(session);
        }

        return Task.FromResult(progress);
    }

    public Task<WorkflowProgressResponse> SubmitHumanDecisionAsync(
        string planId,
        string executionId,
        SubmitHumanDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = _sessions.Get(planId)
                      ?? throw new InvalidOperationException($"Plan '{planId}' not found.");

        var decision = new HumanDecisionRecord
        {
            Decision = request.Decision,
            Notes = request.Notes,
            SubmittedAt = DateTimeOffset.UtcNow
        };

        var progress = _simulator.CompleteWithDecision(executionId, decision);
        session.Status = progress.Status;
        session.HumanDecision = decision;
        _sessions.Update(session);

        return Task.FromResult(progress);
    }

    private static PlanDetailResponse ToDetail(PlanSession session)
    {
        var actions = new List<string>();
        if (session.Status is WorkflowRunStatus.Pending)
        {
            actions.Add("StartWorkflow");
        }

        if (session.Status is WorkflowRunStatus.AwaitingHumanApproval)
        {
            actions.Add("SubmitApproval");
        }

        return new PlanDetailResponse
        {
            PlanId = session.PlanId,
            ScenarioId = session.ScenarioId,
            Title = session.Title,
            Description = session.Description,
            Context = session.Context,
            Status = session.Status,
            ExecutionId = session.ExecutionId,
            AllowedActions = actions
        };
    }
}
