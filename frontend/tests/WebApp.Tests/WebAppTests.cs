using System.Net;
using System.Text;
using System.Text.Json;
using Cohere.InventoryAndTrend.WebApp.Contracts;
using Cohere.InventoryAndTrend.WebApp.Contracts.Api.Backend;
using Cohere.InventoryAndTrend.WebApp.Models;
using Cohere.InventoryAndTrend.WebApp.Services;
using Cohere.InventoryAndTrend.WebApp.State;

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
        return JsonSerializer.Deserialize<BackendBasicWorkflowStatusResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Fixture could not be deserialized.");
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
