using System.Text.Json;
using Cohere.InventoryAndTrend.WebApp.Configuration;
using Cohere.InventoryAndTrend.WebApp.Contracts;
using Cohere.InventoryAndTrend.WebApp.Models;
using Microsoft.Extensions.Options;

namespace Cohere.InventoryAndTrend.WebApp.Services;

public sealed class DatasetSeedCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _rootPath;
    private IReadOnlyList<SeedPlanDefinition>? _cache;

    public DatasetSeedCatalogService(IOptions<DatasetSeedOptions> options, IWebHostEnvironment environment)
    {
        var configured = options.Value.RootPath;
        _rootPath = Path.IsPathRooted(configured)
            ? configured
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configured));
    }

    public string RootPath => _rootPath;

    public IReadOnlyList<SeedPlanDefinition> GetAll()
    {
        _cache ??= LoadCatalog();
        return _cache;
    }

    public SeedPlanDefinition? GetById(string scenarioId) =>
        GetAll().FirstOrDefault(s => string.Equals(s.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));

    public string ReadStageOutputJson(SeedPlanDefinition scenario, WorkflowStageKey stageKey)
    {
        var fileName = stageKey switch
        {
            WorkflowStageKey.SignalIngestion => "signal-ingestion.json",
            WorkflowStageKey.FeatureAndCausality => "feature-and-causality.json",
            WorkflowStageKey.Forecasting => "forecasting.json",
            WorkflowStageKey.ReplenishmentAndAllocation => "replenishment-and-allocation.json",
            WorkflowStageKey.PlannerCopilot => "planner-copilot.json",
            _ => throw new ArgumentOutOfRangeException(nameof(stageKey))
        };

        var path = Path.Combine(
            _rootPath,
            "scenarios",
            scenario.ScenarioFolder,
            "executions",
            scenario.ExecutionProfile,
            fileName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Seed output not found: {path}");
        }

        return File.ReadAllText(path);
    }

    private IReadOnlyList<SeedPlanDefinition> LoadCatalog()
    {
        var catalogPath = Path.Combine(_rootPath, "catalog.json");
        if (!File.Exists(catalogPath))
        {
            throw new FileNotFoundException($"Seed catalog not found: {catalogPath}");
        }

        var json = File.ReadAllText(catalogPath);
        var entries = JsonSerializer.Deserialize<List<CatalogEntry>>(json, JsonOptions)
                      ?? throw new InvalidOperationException("Seed catalog is empty.");

        var results = new List<SeedPlanDefinition>();
        foreach (var entry in entries)
        {
            var scenarioPath = Path.Combine(_rootPath, "scenarios", entry.Folder, "scenario.json");
            if (!File.Exists(scenarioPath))
            {
                continue;
            }

            var scenarioJson = File.ReadAllText(scenarioPath);
            var scenario = JsonSerializer.Deserialize<ScenarioFile>(scenarioJson, JsonOptions)
                           ?? new ScenarioFile();

            results.Add(new SeedPlanDefinition
            {
                ScenarioId = entry.ScenarioId,
                Title = scenario.Title ?? entry.Title,
                Description = scenario.Description ?? entry.Description,
                OutcomeTag = ParseOutcomeTag(entry.OutcomeTag ?? scenario.OutcomeTag),
                Context = scenario.Context ?? new PlanContext(),
                ScenarioFolder = entry.Folder,
                ExecutionProfile = entry.ExecutionProfile ?? "default"
            });
        }

        return results;
    }

    private static ScenarioOutcomeTag ParseOutcomeTag(string? value) =>
        Enum.TryParse<ScenarioOutcomeTag>(value, true, out var parsed)
            ? parsed
            : ScenarioOutcomeTag.HealthyRun;

    private sealed class CatalogEntry
    {
        public string ScenarioId { get; set; } = string.Empty;

        public string Folder { get; set; } = string.Empty;

        public string? Title { get; set; }

        public string? Description { get; set; }

        public string? OutcomeTag { get; set; }

        public string? ExecutionProfile { get; set; }
    }

    private sealed class ScenarioFile
    {
        public string? Title { get; set; }

        public string? Description { get; set; }

        public string? OutcomeTag { get; set; }

        public PlanContext? Context { get; set; }
    }
}
