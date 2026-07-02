using System.Net.Http.Json;
using GrokInventoryAndTrend.WebApp.Contracts;
using GrokInventoryAndTrend.WebApp.Contracts.Api.Backend;

namespace GrokInventoryAndTrend.WebApp.Services;

/// <summary>
/// HTTP client for remote backend integration.
/// Adapts case-based backend endpoints to the UI-facing IPlanningApiClient contract.
/// </summary>
public sealed class PlanningApiClient : IPlanningApiClient
{
    private readonly HttpClient _httpClient;
    private readonly PlanSessionStore _sessions;
    private readonly BackendWorkflowMapper _mapper;
    private IReadOnlyList<SeedPlanDefinitionDto>? _scenarioCache;

    public PlanningApiClient(
        HttpClient httpClient,
        PlanSessionStore sessions,
        BackendWorkflowMapper mapper)
    {
        _httpClient = httpClient;
        _sessions = sessions;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<SeedPlanDefinitionDto>> GetScenariosAsync(CancellationToken cancellationToken = default)
    {
        _scenarioCache ??= await FetchScenariosAsync(cancellationToken);
        return _scenarioCache;
    }

    public async Task<PlanDetailResponse> CreatePlanAsync(string scenarioId, CancellationToken cancellationToken = default)
    {
        var scenario = await GetScenarioByIdAsync(scenarioId, cancellationToken)
                       ?? throw new InvalidOperationException($"Case '{scenarioId}' not found.");

        var session = CreateSessionFromScenario(scenario);
        _sessions.PrepareActiveSlot(scenarioId, session);

        return ToDetailFromScenario(scenario);
    }

    public async Task<PlanDetailResponse?> GetPlanAsync(
        string planId,
        string? executionId = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(executionId))
        {
            var executionSession = _sessions.GetByExecutionId(executionId);
            return executionSession is null ? null : ToDetail(executionSession);
        }

        var activeSession = _sessions.Get(planId);
        if (activeSession is not null && string.IsNullOrWhiteSpace(activeSession.ExecutionId))
        {
            return ToDetail(activeSession);
        }

        var scenario = await GetScenarioByIdAsync(planId, cancellationToken);
        return scenario is null ? null : ToDetailFromScenario(scenario);
    }

    public async Task<StartWorkflowResponse> StartWorkflowAsync(string planId, CancellationToken cancellationToken = default)
    {
        var session = await ResolveSessionForWorkflowStartAsync(planId, cancellationToken);

        if (string.IsNullOrWhiteSpace(session.CaseId))
        {
            throw new InvalidOperationException($"Plan '{planId}' has no associated backend case.");
        }

        using var response = await _httpClient.PostAsync(
            $"api/inventory-planning/cases/{session.CaseId}/workflow/basic/start",
            content: null,
            cancellationToken);

        await ApiProblemDetails.EnsureSuccessOrThrowAsync(response, cancellationToken);

        var backend = await ReadBackendStatusAsync(response, cancellationToken)
                      ?? throw new InvalidOperationException("Empty start workflow response.");

        session.ExecutionId = backend.ExecutionId;
        session.Status = _mapper.MapStatus(backend.Status);
        session.LastBackendStatus = backend;
        session.CreatedAt = DateTimeOffset.UtcNow;
        _sessions.RegisterExecution(session);
        _sessions.Update(session);

        return new StartWorkflowResponse
        {
            ExecutionId = backend.ExecutionId,
            Status = _mapper.MapBasicWorkflowStatus(backend, planId).Status
        };
    }

    public async Task<WorkflowProgressResponse> GetWorkflowStatusAsync(
        string executionId,
        string? planId = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"api/inventory-planning/executions/{executionId}/basic/status",
            cancellationToken);

        await ApiProblemDetails.EnsureSuccessOrThrowAsync(response, cancellationToken);

        var backend = await ReadBackendStatusAsync(response, cancellationToken)
                      ?? throw new InvalidOperationException("Empty workflow status response.");

        var session = ResolveSession(executionId, planId);
        var mapPlanId = session?.PlanId ?? planId ?? backend.CaseId;
        var progress = _mapper.MapBasicWorkflowStatus(backend, mapPlanId, session?.HumanDecision);

        if (session is not null)
        {
            session.LastBackendStatus = backend;
            session.Status = progress.Status;
            session.ExecutionId = progress.ExecutionId;
            _sessions.Update(session);
        }

        return progress;
    }

    private async Task<PlanSession> ResolveSessionForWorkflowStartAsync(
        string planId,
        CancellationToken cancellationToken)
    {
        var active = _sessions.Get(planId);
        if (active is not null && string.IsNullOrWhiteSpace(active.ExecutionId))
        {
            _sessions.PrepareActiveSlot(planId, active);
            return active;
        }

        var scenario = await GetScenarioByIdAsync(planId, cancellationToken)
                       ?? throw new InvalidOperationException($"Case '{planId}' not found.");

        var session = CreateSessionFromScenario(scenario);
        _sessions.PrepareActiveSlot(planId, session);
        return session;
    }

    private static PlanSession CreateSessionFromScenario(SeedPlanDefinitionDto scenario) =>
        new()
        {
            PlanId = scenario.ScenarioId,
            ScenarioId = scenario.ScenarioId,
            CaseId = scenario.ScenarioId,
            Title = scenario.Title,
            Description = scenario.Description,
            Context = scenario.Context,
            Status = WorkflowRunStatus.Pending
        };

    private async Task<SeedPlanDefinitionDto?> GetScenarioByIdAsync(
        string caseId,
        CancellationToken cancellationToken)
    {
        var scenarios = await GetScenariosAsync(cancellationToken);
        return scenarios.FirstOrDefault(s =>
            string.Equals(s.ScenarioId, caseId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<SeedPlanDefinitionDto>> FetchScenariosAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            "api/inventory-planning/cases",
            cancellationToken);

        await ApiProblemDetails.EnsureSuccessOrThrowAsync(response, cancellationToken);

        var backend = await response.Content.ReadFromJsonAsync<BackendCaseListResponse>(
                          BackendApiJson.Options,
                          cancellationToken)
                      ?? throw new InvalidOperationException("Empty case list response.");

        return backend.Cases.Select(MapScenario).ToList();
    }

    private static SeedPlanDefinitionDto MapScenario(BackendCaseSummaryResponse backendCase) =>
        new()
        {
            ScenarioId = backendCase.CaseId,
            Title = backendCase.Title,
            Description = backendCase.Description,
            OutcomeTag = ParseOutcomeTag(backendCase.OutcomeTag),
            ExpectedOutcome = backendCase.ExpectedOutcome,
            LegacyId = backendCase.LegacyId,
            Context = new PlanContext
            {
                Category = backendCase.Context.Category,
                Campaign = backendCase.Context.Campaign,
                PlanningHorizon = backendCase.Context.PlanningHorizon,
                BudgetCap = backendCase.Context.BudgetCap,
                TargetFillRate = backendCase.Context.TargetFillRate,
                AffectedSkuCount = backendCase.Context.AffectedSkuCount,
                SignalSources = backendCase.Context.SignalSources
            }
        };

    private static ScenarioOutcomeTag ParseOutcomeTag(string? value) =>
        Enum.TryParse<ScenarioOutcomeTag>(value, true, out var parsed)
            ? parsed
            : ScenarioOutcomeTag.HealthyRun;

    private PlanSession? ResolveSession(string executionId, string? planId)
    {
        var session = _sessions.GetByExecutionId(executionId);
        if (session is not null)
        {
            return session;
        }

        if (!string.IsNullOrWhiteSpace(planId))
        {
            session = _sessions.Get(planId);
            if (session is not null
                && string.Equals(session.ExecutionId, executionId, StringComparison.OrdinalIgnoreCase))
            {
                return session;
            }
        }

        return null;
    }

    public Task<WorkflowProgressResponse> SubmitHumanDecisionAsync(
        string planId,
        string executionId,
        SubmitHumanDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = _sessions.GetByExecutionId(executionId)
                      ?? throw new InvalidOperationException(
                          $"Execution '{executionId}' was not found in this session.");

        if (session.LastBackendStatus is null)
        {
            throw new InvalidOperationException(
                $"No workflow status cached for execution '{executionId}'. Poll workflow status before submitting a decision.");
        }

        var decision = new HumanDecisionRecord
        {
            Decision = request.Decision,
            Notes = request.Notes,
            SubmittedAt = DateTimeOffset.UtcNow
        };

        session.HumanDecision = decision;
        var progress = _mapper.MapBasicWorkflowStatus(session.LastBackendStatus, planId, decision);
        session.Status = progress.Status;
        _sessions.Update(session);

        return Task.FromResult(progress);
    }

    private static List<string> BuildAllowedActions(WorkflowRunStatus status)
    {
        var actions = new List<string>();
        if (status is WorkflowRunStatus.Pending)
        {
            actions.Add("StartWorkflow");
        }

        if (status is WorkflowRunStatus.AwaitingHumanApproval)
        {
            actions.Add("SubmitApproval");
        }

        return actions;
    }

    private static async Task<BackendBasicWorkflowStatusResponse?> ReadBackendStatusAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        return await response.Content.ReadFromJsonAsync<BackendBasicWorkflowStatusResponse>(
            BackendApiJson.Options,
            cancellationToken);
    }

    private static PlanDetailResponse ToDetailFromScenario(SeedPlanDefinitionDto scenario) =>
        new()
        {
            PlanId = scenario.ScenarioId,
            ScenarioId = scenario.ScenarioId,
            Title = scenario.Title,
            Description = scenario.Description,
            Context = scenario.Context,
            Status = WorkflowRunStatus.Pending,
            ExecutionId = null,
            AllowedActions = BuildAllowedActions(WorkflowRunStatus.Pending)
        };

    private static PlanDetailResponse ToDetail(PlanSession session) =>
        new()
        {
            PlanId = session.PlanId,
            ScenarioId = session.ScenarioId,
            Title = session.Title,
            Description = session.Description,
            Context = session.Context,
            Status = session.Status,
            ExecutionId = session.ExecutionId,
            AllowedActions = BuildAllowedActions(session.Status)
        };
}

public static class ApiProblemDetails
{
    public static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            $"API request failed ({(int)response.StatusCode}): {body}");
    }
}
