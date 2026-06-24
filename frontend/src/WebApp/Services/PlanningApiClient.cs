using System.Net.Http.Json;
using System.Text.Json;
using Cohere.InventoryAndTrend.WebApp.Configuration;
using Cohere.InventoryAndTrend.WebApp.Contracts;
using Cohere.InventoryAndTrend.WebApp.Contracts.Api.Backend;
using Microsoft.Extensions.Options;

namespace Cohere.InventoryAndTrend.WebApp.Services;

/// <summary>
/// HTTP client for remote backend integration (Phase 2).
/// Wire DTOs and mapper must be updated when backend contract is finalized.
/// </summary>
public sealed class PlanningApiClient : IPlanningApiClient
{
    private readonly HttpClient _httpClient;
    private readonly DatasetSeedCatalogService _catalog;
    private readonly PlanSessionStore _sessions;
    private readonly BackendWorkflowMapper _mapper;

    public PlanningApiClient(
        HttpClient httpClient,
        DatasetSeedCatalogService catalog,
        PlanSessionStore sessions,
        BackendWorkflowMapper mapper)
    {
        _httpClient = httpClient;
        _catalog = catalog;
        _sessions = sessions;
        _mapper = mapper;
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
        throw new NotSupportedException(
            "Remote mode plan creation uses local seed catalog. Ensure a plan session exists or implement backend plan creation when the API is available.");
    }

    public async Task<PlanDetailResponse?> GetPlanAsync(string planId, CancellationToken cancellationToken = default)
    {
        var session = _sessions.Get(planId);
        if (session is not null)
        {
            return new PlanDetailResponse
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

        return null;
    }

    public async Task<StartWorkflowResponse> StartWorkflowAsync(string planId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync(
            $"api/inventory-planning/plans/{planId}/workflow/start",
            content: null,
            cancellationToken);

        await ApiProblemDetails.EnsureSuccessOrThrowAsync(response, cancellationToken);

        var backend = await response.Content.ReadFromJsonAsync<BackendStartWorkflowResponse>(cancellationToken: cancellationToken)
                      ?? throw new InvalidOperationException("Empty start workflow response.");

        var session = _sessions.Get(planId);
        if (session is not null)
        {
            session.ExecutionId = backend.ExecutionId;
            session.Status = _mapper.MapStatus(backend.Status);
            _sessions.Update(session);
        }

        return new StartWorkflowResponse
        {
            ExecutionId = backend.ExecutionId,
            Status = _mapper.MapStatus(backend.Status)
        };
    }

    public async Task<WorkflowProgressResponse> GetWorkflowStatusAsync(string executionId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"api/inventory-planning/executions/{executionId}/status",
            cancellationToken);

        await ApiProblemDetails.EnsureSuccessOrThrowAsync(response, cancellationToken);

        var backend = await response.Content.ReadFromJsonAsync<BackendWorkflowStatusResponse>(cancellationToken: cancellationToken)
                      ?? throw new InvalidOperationException("Empty workflow status response.");

        var progress = _mapper.MapStatusResponse(backend);

        var session = _sessions.Get(progress.PlanId);
        if (session is not null)
        {
            session.Status = progress.Status;
            session.ExecutionId = progress.ExecutionId;
            session.HumanDecision = progress.HumanDecision;
            _sessions.Update(session);
        }

        return progress;
    }

    public async Task<WorkflowProgressResponse> SubmitHumanDecisionAsync(
        string planId,
        string executionId,
        SubmitHumanDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"api/inventory-planning/plans/{planId}/executions/{executionId}/resume",
            request,
            cancellationToken);

        await ApiProblemDetails.EnsureSuccessOrThrowAsync(response, cancellationToken);

        return await GetWorkflowStatusAsync(executionId, cancellationToken);
    }

    private static IReadOnlyList<string> BuildAllowedActions(WorkflowRunStatus status)
    {
        if (status is WorkflowRunStatus.Pending)
        {
            return ["StartWorkflow"];
        }

        if (status is WorkflowRunStatus.AwaitingHumanApproval)
        {
            return ["SubmitApproval"];
        }

        return [];
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
