using System.Net.Http.Json;
using Cohere.InventoryAndTrend.WebApp.Contracts;
using Cohere.InventoryAndTrend.WebApp.Contracts.Api.Backend;
using Cohere.InventoryAndTrend.WebApp.Services;

namespace Cohere.InventoryAndTrend.WebApp.Services;

/// <summary>
/// HTTP client for remote backend integration.
/// Adapts case-based backend endpoints to the UI-facing IPlanningApiClient contract.
/// </summary>
public sealed class PlanningApiClient : IPlanningApiClient
{
    private readonly HttpClient _httpClient;
    private readonly BackendCaseCatalogService _caseCatalog;
    private readonly PlanSessionStore _sessions;
    private readonly BackendWorkflowMapper _mapper;

    public PlanningApiClient(
        HttpClient httpClient,
        BackendCaseCatalogService caseCatalog,
        PlanSessionStore sessions,
        BackendWorkflowMapper mapper)
    {
        _httpClient = httpClient;
        _caseCatalog = caseCatalog;
        _sessions = sessions;
        _mapper = mapper;
    }

    public Task<IReadOnlyList<SeedPlanDefinitionDto>> GetScenariosAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_caseCatalog.GetAll());
    }

    public Task<PlanDetailResponse> CreatePlanAsync(string scenarioId, CancellationToken cancellationToken = default)
    {
        var scenario = _caseCatalog.GetByCaseId(scenarioId)
                       ?? throw new InvalidOperationException($"Case '{scenarioId}' not found.");

        var planId = Guid.NewGuid().ToString("N")[..12];
        var session = new PlanSession
        {
            PlanId = planId,
            ScenarioId = scenario.ScenarioId,
            CaseId = scenario.ScenarioId,
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

    public async Task<StartWorkflowResponse> StartWorkflowAsync(string planId, CancellationToken cancellationToken = default)
    {
        var session = _sessions.Get(planId)
                      ?? throw new InvalidOperationException($"Plan '{planId}' not found.");

        if (string.IsNullOrWhiteSpace(session.CaseId))
        {
            throw new InvalidOperationException($"Plan '{planId}' has no associated backend case.");
        }

        using var response = await _httpClient.PostAsync(
            $"api/inventory-planning/cases/{session.CaseId}/workflow/basic/start",
            content: null,
            cancellationToken);

        await ApiProblemDetails.EnsureSuccessOrThrowAsync(response, cancellationToken);

        var backend = await response.Content.ReadFromJsonAsync<BackendBasicWorkflowStatusResponse>(cancellationToken: cancellationToken)
                      ?? throw new InvalidOperationException("Empty start workflow response.");

        session.ExecutionId = backend.ExecutionId;
        session.Status = _mapper.MapStatus(backend.Status);
        session.LastBackendStatus = backend;
        _sessions.Update(session);

        return new StartWorkflowResponse
        {
            ExecutionId = backend.ExecutionId,
            Status = _mapper.MapBasicWorkflowStatus(backend, planId).Status
        };
    }

    public async Task<WorkflowProgressResponse> GetWorkflowStatusAsync(string executionId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"api/inventory-planning/executions/{executionId}/basic/status",
            cancellationToken);

        await ApiProblemDetails.EnsureSuccessOrThrowAsync(response, cancellationToken);

        var backend = await response.Content.ReadFromJsonAsync<BackendBasicWorkflowStatusResponse>(cancellationToken: cancellationToken)
                      ?? throw new InvalidOperationException("Empty workflow status response.");

        var session = _sessions.GetByExecutionId(executionId)
                      ?? throw new InvalidOperationException($"No session found for execution '{executionId}'.");

        session.LastBackendStatus = backend;
        var progress = _mapper.MapBasicWorkflowStatus(backend, session.PlanId, session.HumanDecision);
        session.Status = progress.Status;
        session.ExecutionId = progress.ExecutionId;
        _sessions.Update(session);

        return progress;
    }

    public Task<WorkflowProgressResponse> SubmitHumanDecisionAsync(
        string planId,
        string executionId,
        SubmitHumanDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = _sessions.Get(planId)
                      ?? throw new InvalidOperationException($"Plan '{planId}' not found.");

        if (session.LastBackendStatus is null)
        {
            throw new InvalidOperationException(
                $"No workflow status cached for plan '{planId}'. Poll workflow status before submitting a decision.");
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
