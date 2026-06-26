using System.Net;
using System.Text;
using System.Text.Json;
using Cohere.InventoryAndTrend.WebApp.Configuration;
using Cohere.InventoryAndTrend.WebApp.Contracts;
using Cohere.InventoryAndTrend.WebApp.Contracts.Api.Backend;
using Cohere.InventoryAndTrend.WebApp.Models;
using Cohere.InventoryAndTrend.WebApp.Services;
using Cohere.InventoryAndTrend.WebApp.State;
using Microsoft.Extensions.Options;

namespace Cohere.InventoryAndTrend.WebApp.Tests;

public class AgentOutputParserTests
{
    private readonly AgentOutputParser _parser = new();

    [Fact]
    public void ParseForecasting_ReadsExtendedFields()
    {
        var json = """
                   {
                     "summary": "Forecast ready",
                     "decision": "Forecast Ready",
                     "evidence": "Stable trend",
                     "confidenceLevel": "High",
                     "anomalies": ["Spike"],
                     "keyMetrics": ["+10%"]
                   }
                   """;

        var result = _parser.Parse(WorkflowStageKey.Forecasting, json);
        var forecast = Assert.IsType<ForecastingStageResult>(result);
        Assert.Equal("High", forecast.ConfidenceLevel);
        Assert.Single(forecast.Anomalies);
    }

    [Fact]
    public void Parse_StripsAssistantPrefix()
    {
        var json = "[assistant] {\"summary\":\"Done\",\"decision\":\"Proceed\",\"evidence\":\"OK\"}";
        var result = _parser.Parse(WorkflowStageKey.SignalIngestion, json);
        Assert.Equal("Done", result.Summary);
    }

    [Fact]
    public void Parse_FallsBackToPlainTextWhenNotJson()
    {
        var result = _parser.Parse(WorkflowStageKey.Forecasting, "Plain agent response text");
        Assert.Equal("Plain agent response text", result.Summary);
    }
}

public class BackendCaseCatalogServiceTests
{
    [Fact]
    public void LoadCatalog_ReturnsFiveBackendCases()
    {
        var service = TestSupport.CreateCaseCatalogServiceFromAppSettingsRelativePath();
        var cases = service.GetAll();
        Assert.Equal(5, cases.Count);
        Assert.Contains(cases, c => c.ScenarioId == "case-01");
    }

    [Fact]
    public void GetByCaseId_ReturnsMatchingCase()
    {
        var service = TestSupport.CreateCaseCatalogService();
        var caseEntry = service.GetByCaseId("case-03");
        Assert.NotNull(caseEntry);
        Assert.Contains("Supplier delay", caseEntry!.Title);
    }

    [Fact]
    public void ConfiguredRelativePath_ResolvesToRepoRootDatasetSeed()
    {
        var service = TestSupport.CreateCaseCatalogServiceFromAppSettingsRelativePath();
        var catalogPath = Path.Combine(TestSupport.DatasetSeedRoot, "cases", "catalog.json");

        Assert.True(File.Exists(catalogPath), $"Expected catalog at {catalogPath}");
        Assert.Equal(5, service.GetAll().Count);
    }
}

public class BackendWorkflowMapperTests
{
    private static BackendWorkflowMapper CreateMapper() =>
        new(new AgentOutputParser());

    private static BackendBasicWorkflowStatusResponse LoadFixture(string fileName)
    {
        var json = TestSupport.ReadBackendFixture(fileName);
        return JsonSerializer.Deserialize<BackendBasicWorkflowStatusResponse>(json, BackendApiJson.Options)
               ?? throw new InvalidOperationException("Fixture could not be deserialized.");
    }

    [Fact]
    public void MapStatus_NormalisesInProgress()
    {
        var mapper = CreateMapper();
        Assert.Equal(WorkflowRunStatus.Running, mapper.MapStatus("InProgress"));
        Assert.Equal(WorkflowRunStatus.Running, mapper.MapStatus("Running"));
    }

    [Fact]
    public void MapBasicWorkflowStatus_RunningBuildsStagesFromAgentOutputs()
    {
        var mapper = CreateMapper();
        var backend = LoadFixture("running-status.json");

        var progress = mapper.MapBasicWorkflowStatus(backend, "plan-1");

        Assert.Equal(WorkflowRunStatus.Running, progress.Status);
        Assert.Equal(WorkflowStageKey.FeatureAndCausality, progress.CurrentStage);
        Assert.Equal("Completed", progress.Stages[0].Status);
        Assert.Equal("Running", progress.Stages[1].Status);
        Assert.NotNull(progress.Stages[0].Output);
    }

    [Fact]
    public void MapBasicWorkflowStatus_ObjectAgentOutputsBuildsStages()
    {
        var mapper = CreateMapper();
        var backend = LoadFixture("running-with-object-outputs.json");

        var progress = mapper.MapBasicWorkflowStatus(backend, "plan-1");

        Assert.Equal(WorkflowRunStatus.Running, progress.Status);
        Assert.Equal("Proceed", progress.Stages[0].Output!.Decision);
        Assert.Equal("Re-run Required", progress.Stages[1].Output!.Decision);
        Assert.Equal("Insufficient History", progress.Stages[2].Output!.Decision);
        Assert.Equal(WorkflowStageKey.ReplenishmentAndAllocation, progress.CurrentStage);
    }

    [Fact]
    public void MapBasicWorkflowStatus_LiveApiStringOutputsBuildsCompletedStages()
    {
        var mapper = CreateMapper();
        var backend = LoadFixture("live-status-sample.json");

        var progress = mapper.MapBasicWorkflowStatus(backend, "plan-1");

        Assert.Equal(WorkflowRunStatus.Running, progress.Status);
        Assert.Equal("Completed", progress.Stages[0].Status);
        Assert.Equal("Insufficient Data", progress.Stages[0].Output!.Decision);
        Assert.Equal("Completed", progress.Stages[1].Status);
        Assert.Equal("Re-run Required", progress.Stages[1].Output!.Decision);
        Assert.NotNull(progress.Stages[0].Output!.Summary);
    }

    [Fact]
    public void MapBasicWorkflowStatus_CompletedMapsToAwaitingHumanApproval()
    {
        var mapper = CreateMapper();
        var backend = LoadFixture("completed-status.json");

        var progress = mapper.MapBasicWorkflowStatus(backend, "plan-1");

        Assert.Equal(WorkflowRunStatus.AwaitingHumanApproval, progress.Status);
        Assert.Equal(5, progress.Stages.Count(s => s.Status == "Completed"));
        Assert.Equal(WorkflowStageKey.PlannerCopilot, progress.CurrentStage);
    }

    [Fact]
    public void MapBasicWorkflowStatus_CompletedWithDecisionMapsToTerminalState()
    {
        var mapper = CreateMapper();
        var backend = LoadFixture("completed-status.json");
        var decision = new HumanDecisionRecord
        {
            Decision = HumanDecisionType.Approve,
            Notes = "Looks good",
            SubmittedAt = DateTimeOffset.UtcNow
        };

        var progress = mapper.MapBasicWorkflowStatus(backend, "plan-1", decision);

        Assert.Equal(WorkflowRunStatus.Completed, progress.Status);
        Assert.Equal(decision, progress.HumanDecision);
    }

    [Fact]
    public void MapBasicWorkflowStatus_FailedUsesFailureReason()
    {
        var mapper = CreateMapper();
        var backend = LoadFixture("failed-status.json");

        var progress = mapper.MapBasicWorkflowStatus(backend, "plan-1");

        Assert.Equal(WorkflowRunStatus.Failed, progress.Status);
        Assert.Contains("forecasting", progress.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
}

public class PlanningApiClientTests
{
    [Fact]
    public async Task StartWorkflowAsync_PostsToCaseBasedEndpoint()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(TestSupport.ReadBackendFixture("running-status.json"), Encoding.UTF8, "application/json")
            });

        var client = CreateClient(handler, out var sessions);
        var plan = await client.CreatePlanAsync("case-01");
        var start = await client.StartWorkflowAsync(plan.PlanId);

        Assert.Equal("abc123def4567890abcdef1234567890", start.ExecutionId);
        Assert.Contains("/cases/case-01/workflow/basic/start", handler.LastRequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task GetWorkflowStatusAsync_UsesBasicStatusEndpoint()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(TestSupport.ReadBackendFixture("running-status.json"), Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(TestSupport.ReadBackendFixture("completed-status.json"), Encoding.UTF8, "application/json")
            };
        });

        var client = CreateClient(handler, out _);
        var plan = await client.CreatePlanAsync("case-01");
        var start = await client.StartWorkflowAsync(plan.PlanId);
        var progress = await client.GetWorkflowStatusAsync(start.ExecutionId);

        Assert.Contains("/executions/abc123def4567890abcdef1234567890/basic/status", handler.LastRequestUri?.AbsolutePath);
        Assert.Equal(WorkflowRunStatus.AwaitingHumanApproval, progress.Status);
    }

    [Fact]
    public async Task GetWorkflowStatusAsync_MapsAgentOutputsWithoutSession()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    TestSupport.ReadBackendFixture("live-status-sample.json"),
                    Encoding.UTF8,
                    "application/json")
            });

        var client = CreateClient(handler, out var sessions);
        var progress = await client.GetWorkflowStatusAsync("616385c9a92e4be9a4047614ecb50792", "plan-missing");

        Assert.Equal("Insufficient Data", progress.Stages[0].Output!.Decision);
        Assert.Equal("Completed", progress.Stages[0].Status);
        Assert.Null(sessions.GetByExecutionId("616385c9a92e4be9a4047614ecb50792"));
    }

    [Fact]
    public async Task SubmitHumanDecisionAsync_RecordsDecisionWithoutHttpCall()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(TestSupport.ReadBackendFixture("completed-status.json"), Encoding.UTF8, "application/json")
            });

        var client = CreateClient(handler, out var sessions);
        var plan = await client.CreatePlanAsync("case-01");
        var start = await client.StartWorkflowAsync(plan.PlanId);
        await client.GetWorkflowStatusAsync(start.ExecutionId);

        var requestCountBefore = handler.RequestCount;
        var progress = await client.SubmitHumanDecisionAsync(
            plan.PlanId,
            start.ExecutionId,
            new SubmitHumanDecisionRequest { Decision = HumanDecisionType.Reject, Notes = "Too risky" });

        Assert.Equal(requestCountBefore, handler.RequestCount);
        Assert.Equal(WorkflowRunStatus.Failed, progress.Status);
        Assert.Equal(HumanDecisionType.Reject, sessions.Get(plan.PlanId)!.HumanDecision!.Decision);
    }

    [Fact]
    public async Task StartWorkflowAsync_UpdatesAllowedActionsOnSubsequentGetPlan()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(TestSupport.ReadBackendFixture("running-status.json"), Encoding.UTF8, "application/json")
            });

        var client = CreateClient(handler, out _);
        var plan = await client.CreatePlanAsync("case-01");
        Assert.Contains("StartWorkflow", plan.AllowedActions);

        await client.StartWorkflowAsync(plan.PlanId);
        var refreshed = await client.GetPlanAsync(plan.PlanId);

        Assert.NotNull(refreshed);
        Assert.DoesNotContain("StartWorkflow", refreshed!.AllowedActions);
        Assert.Equal(WorkflowRunStatus.Running, refreshed.Status);
    }

    private static PlanningApiClient CreateClient(HttpMessageHandler handler, out PlanSessionStore sessions)
    {
        sessions = new PlanSessionStore();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5038/") };
        var caseCatalog = TestSupport.CreateCaseCatalogServiceFromAppSettingsRelativePath();
        var mapper = new BackendWorkflowMapper(new AgentOutputParser());
        return new PlanningApiClient(httpClient, caseCatalog, sessions, mapper);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _responder = responder;

        public Uri? LastRequestUri { get; private set; }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            return Task.FromResult(_responder(request));
        }
    }
}

public class ScenarioPickerFilterTests
{
    [Fact]
    public void Matches_FiltersByOutcomeTag()
    {
        var scenario = new SeedPlanDefinitionDto
        {
            ScenarioId = "x",
            Title = "Test",
            Description = "Desc",
            OutcomeTag = ScenarioOutcomeTag.BudgetPressure,
            Context = new PlanContext { Category = "Cat", Campaign = "Camp" }
        };

        Assert.True(ScenarioPickerFilter.Matches(scenario, null, ScenarioOutcomeTag.BudgetPressure));
        Assert.False(ScenarioPickerFilter.Matches(scenario, null, ScenarioOutcomeTag.HealthyRun));
    }
}

public class WorkflowStageUiTests
{
    [Fact]
    public void ParseStageKey_AcceptsSnakeCase()
    {
        Assert.Equal(WorkflowStageKey.SignalIngestion, WorkflowStageUi.ParseStageKey("signal_ingestion"));
        Assert.Equal(WorkflowStageKey.PlannerCopilot, WorkflowStageUi.ParseStageKey("planner-copilot"));
    }

    [Fact]
    public void OrderedStages_HasFiveEntries()
    {
        Assert.Equal(5, WorkflowStageUi.OrderedStages.Count);
    }
}

public class PlanWorkspaceStateTests
{
    [Fact]
    public async Task StartWorkflowAsync_CompletesWhileUiRefreshIsPending()
    {
        var refreshGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new DelayedPlanningApiClient();
        var state = new PlanWorkspaceState(
            client,
            Options.Create(new WorkflowPollingOptions { IntervalSeconds = 60 }));

        state.RegisterUiRefresh(() => refreshGate.Task);
        await state.LoadAsync("plan-test", executionId: null);

        var startTask = state.StartWorkflowAsync();
        var completed = await Task.WhenAny(startTask, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(startTask, completed);
        await startTask;

        refreshGate.SetResult();
        await state.DisposeAsync();
    }

    [Fact]
    public async Task StartWorkflowAsync_RefreshesAllowedActionsAfterStart()
    {
        var client = new DelayedPlanningApiClient();
        var state = new PlanWorkspaceState(
            client,
            Options.Create(new WorkflowPollingOptions { IntervalSeconds = 60 }));

        await state.LoadAsync("plan-test", executionId: null);
        Assert.Contains("StartWorkflow", state.CurrentPlan!.AllowedActions);

        await state.StartWorkflowAsync();

        Assert.DoesNotContain("StartWorkflow", state.CurrentPlan!.AllowedActions);
        Assert.NotNull(state.WorkflowProgress);
        Assert.Equal(WorkflowRunStatus.Running, state.WorkflowProgress!.Status);

        await state.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_ClearsUiRefreshCallback()
    {
        var refreshCount = 0;
        var client = new DelayedPlanningApiClient();
        var state = new PlanWorkspaceState(
            client,
            Options.Create(new WorkflowPollingOptions { IntervalSeconds = 60 }));

        state.RegisterUiRefresh(() =>
        {
            Interlocked.Increment(ref refreshCount);
            return Task.CompletedTask;
        });

        await state.LoadAsync("plan-test", executionId: null);
        var countAfterLoad = refreshCount;

        await state.DisposeAsync();
        await state.StartWorkflowAsync();

        Assert.Equal(countAfterLoad, refreshCount);
    }

    [Fact]
    public async Task UnregisterUiRefresh_PreventsFurtherCallbacks()
    {
        var refreshCount = 0;
        var client = new DelayedPlanningApiClient();
        var state = new PlanWorkspaceState(
            client,
            Options.Create(new WorkflowPollingOptions { IntervalSeconds = 60 }));

        state.RegisterUiRefresh(() =>
        {
            Interlocked.Increment(ref refreshCount);
            return Task.CompletedTask;
        });

        await state.LoadAsync("plan-test", executionId: null);
        var countAfterLoad = refreshCount;

        state.UnregisterUiRefresh();
        await state.StartWorkflowAsync();

        Assert.Equal(countAfterLoad, refreshCount);
        await state.DisposeAsync();
    }

    [Fact]
    public async Task ReloadWithExecutionId_StopsPreviousPollBeforeStartingNew()
    {
        var client = new DelayedPlanningApiClient();
        var state = new PlanWorkspaceState(
            client,
            Options.Create(new WorkflowPollingOptions { IntervalSeconds = 1 }));

        await state.LoadAsync("plan-test", executionId: null);
        await state.StartWorkflowAsync();
        Assert.True(state.IsPollingWorkflow);

        await state.LoadAsync("plan-test", executionId: "exec-test-001");
        Assert.True(state.IsPollingWorkflow);

        await state.DisposeAsync();
        Assert.False(state.IsPollingWorkflow);
    }

    [Fact]
    public async Task PollFailure_SetsPollErrorWithoutTopLevelError()
    {
        var client = new FlakyStatusPlanningApiClient();
        var state = new PlanWorkspaceState(
            client,
            Options.Create(new WorkflowPollingOptions { IntervalSeconds = 1, MaxDurationMinutes = 1 }));

        await state.LoadAsync("plan-test", executionId: null);
        await state.StartWorkflowAsync();

        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Null(state.Error);
        Assert.NotNull(state.PollError);
        Assert.True(state.IsPollingWorkflow);

        await state.DisposeAsync();
    }

    private sealed class FlakyStatusPlanningApiClient : IPlanningApiClient
    {
        private readonly PlanSession _session = new()
        {
            PlanId = "plan-test",
            ScenarioId = "case-01",
            CaseId = "case-01",
            Title = "Test plan",
            Status = WorkflowRunStatus.Pending
        };

        private int _statusCalls;

        public Task<IReadOnlyList<SeedPlanDefinitionDto>> GetScenariosAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PlanDetailResponse> CreatePlanAsync(string scenarioId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PlanDetailResponse?> GetPlanAsync(string planId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PlanDetailResponse?>(ToDetail(_session));

        public Task<StartWorkflowResponse> StartWorkflowAsync(string planId, CancellationToken cancellationToken = default)
        {
            _session.ExecutionId = "exec-test-001";
            _session.Status = WorkflowRunStatus.Running;

            return Task.FromResult(new StartWorkflowResponse
            {
                ExecutionId = _session.ExecutionId,
                Status = WorkflowRunStatus.Running
            });
        }

        public Task<WorkflowProgressResponse> GetWorkflowStatusAsync(
            string executionId,
            string? planId = null,
            CancellationToken cancellationToken = default)
        {
            _statusCalls++;
            if (_statusCalls > 1)
            {
                throw new InvalidOperationException("Transient status failure.");
            }

            return Task.FromResult(new WorkflowProgressResponse
            {
                PlanId = _session.PlanId,
                ExecutionId = executionId,
                Status = WorkflowRunStatus.Running,
                StatusMessage = "Workflow in progress…",
                Stages = []
            });
        }

        public Task<WorkflowProgressResponse> SubmitHumanDecisionAsync(
            string planId,
            string executionId,
            SubmitHumanDecisionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private static PlanDetailResponse ToDetail(PlanSession session) =>
            new()
            {
                PlanId = session.PlanId,
                ScenarioId = session.ScenarioId,
                Title = session.Title,
                Status = session.Status,
                ExecutionId = session.ExecutionId,
                AllowedActions = session.Status is WorkflowRunStatus.Pending ? ["StartWorkflow"] : []
            };
    }

    private sealed class DelayedPlanningApiClient : IPlanningApiClient
    {
        private readonly PlanSession _session = new()
        {
            PlanId = "plan-test",
            ScenarioId = "case-01",
            CaseId = "case-01",
            Title = "Test plan",
            Status = WorkflowRunStatus.Pending
        };

        public Task<IReadOnlyList<SeedPlanDefinitionDto>> GetScenariosAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PlanDetailResponse> CreatePlanAsync(string scenarioId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PlanDetailResponse?> GetPlanAsync(string planId, CancellationToken cancellationToken = default)
        {
            if (!string.Equals(planId, _session.PlanId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<PlanDetailResponse?>(null);
            }

            return Task.FromResult<PlanDetailResponse?>(ToDetail(_session));
        }

        public async Task<StartWorkflowResponse> StartWorkflowAsync(string planId, CancellationToken cancellationToken = default)
        {
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            _session.ExecutionId = "exec-test-001";
            _session.Status = WorkflowRunStatus.Running;

            return new StartWorkflowResponse
            {
                ExecutionId = _session.ExecutionId,
                Status = WorkflowRunStatus.Running
            };
        }

        public async Task<WorkflowProgressResponse> GetWorkflowStatusAsync(
            string executionId,
            string? planId = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);

            return new WorkflowProgressResponse
            {
                PlanId = _session.PlanId,
                ExecutionId = executionId,
                Status = WorkflowRunStatus.Running,
                StatusMessage = "Workflow in progress…",
                Stages = []
            };
        }

        public Task<WorkflowProgressResponse> SubmitHumanDecisionAsync(
            string planId,
            string executionId,
            SubmitHumanDecisionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
                Status = session.Status,
                ExecutionId = session.ExecutionId,
                AllowedActions = actions
            };
        }
    }
}

public class StageBodyUiTests
{
    [Fact]
    public void Resolve_ReturnsRunningWhenStageIsRunningWithoutOutput()
    {
        Assert.Equal(StageBodyDisplay.Running, StageBodyUi.Resolve("Running", hasOutput: false));
    }

    [Fact]
    public void Resolve_ReturnsPendingWhenStageIsPendingWithoutOutput()
    {
        Assert.Equal(StageBodyDisplay.Pending, StageBodyUi.Resolve("Pending", hasOutput: false));
    }

    [Fact]
    public void Resolve_ReturnsPanelWhenOutputExists()
    {
        Assert.Equal(StageBodyDisplay.Panel, StageBodyUi.Resolve("Running", hasOutput: true));
        Assert.Equal(StageBodyDisplay.Panel, StageBodyUi.Resolve("Pending", hasOutput: true));
    }
}
