using Cohere.InventoryAndTrend.WebApp.Contracts;
using Cohere.InventoryAndTrend.WebApp.Models;
using Cohere.InventoryAndTrend.WebApp.Services;
using Cohere.InventoryAndTrend.WebApp.State;

namespace Cohere.InventoryAndTrend.WebApp.Tests;

public class DatasetSeedCatalogServiceTests
{
    [Fact]
    public void LoadCatalog_ReturnsFourScenarios()
    {
        var service = TestSupport.CreateCatalogService();
        var scenarios = service.GetAll();
        Assert.Equal(4, scenarios.Count);
    }

    [Fact]
    public void ReadStageOutput_ParsesSummerCampaignSignalIngestion()
    {
        var service = TestSupport.CreateCatalogService();
        var scenario = service.GetById("summer-campaign-category-x");
        Assert.NotNull(scenario);

        var json = service.ReadStageOutputJson(scenario!, WorkflowStageKey.SignalIngestion);
        Assert.Contains("Signals Validated", json);
    }

    [Fact]
    public void ConfiguredRelativePath_ResolvesToFrontendDatasetSeed()
    {
        var service = TestSupport.CreateCatalogServiceFromAppSettingsRelativePath();
        var catalogPath = Path.Combine(service.RootPath, "catalog.json");

        Assert.True(File.Exists(catalogPath), $"Expected catalog at {catalogPath}");
        Assert.Equal(TestSupport.DatasetSeedRoot, Path.GetFullPath(service.RootPath));
        Assert.Equal(4, service.GetAll().Count);
    }
}

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
}

public class BackendWorkflowMapperTests
{
    [Fact]
    public void MapStatus_NormalisesInProgress()
    {
        var mapper = new BackendWorkflowMapper(new AgentOutputParser());
        Assert.Equal(WorkflowRunStatus.Running, mapper.MapStatus("InProgress"));
        Assert.Equal(WorkflowRunStatus.AwaitingHumanApproval, mapper.MapStatus("WaitingForApproval"));
    }
}

public class LocalPlanningSimulatorTests
{
    [Fact]
    public void Advance_CompletesAllStagesAndAwaitApproval()
    {
        var catalog = TestSupport.CreateCatalogService();
        var scenario = catalog.GetById("summer-campaign-category-x")!;
        var parser = new AgentOutputParser();
        var simulator = new LocalPlanningSimulator(parser);

        var run = simulator.Start(
            "plan-1",
            scenario,
            stage => catalog.ReadStageOutputJson(scenario, stage));

        WorkflowProgressResponse? progress = null;
        for (var i = 0; i < 10; i++)
        {
            progress = simulator.AdvanceAndBuildStatus(run.ExecutionId);
            if (progress.Status == WorkflowRunStatus.AwaitingHumanApproval)
            {
                break;
            }
        }

        Assert.NotNull(progress);
        Assert.Equal(WorkflowRunStatus.AwaitingHumanApproval, progress!.Status);
        Assert.Equal(5, progress.Stages.Count(s => s.Status == "Completed"));
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
