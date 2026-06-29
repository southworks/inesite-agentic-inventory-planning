using System.Text.Json;
using GrokInventoryAndTrend.WebApp.Configuration;
using GrokInventoryAndTrend.WebApp.Contracts;
using Microsoft.Extensions.Options;

namespace GrokInventoryAndTrend.WebApp.Services;

public sealed class BackendCaseCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _catalogPath;
    private IReadOnlyList<SeedPlanDefinitionDto>? _cache;

    public BackendCaseCatalogService(IOptions<DatasetSeedOptions> options, IWebHostEnvironment environment)
    {
        var rootPath = options.Value.RootPath;
        var resolvedRoot = Path.IsPathRooted(rootPath)
            ? rootPath
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, rootPath));

        _catalogPath = Path.Combine(resolvedRoot, "cases", "catalog.json");
    }

    public IReadOnlyList<SeedPlanDefinitionDto> GetAll()
    {
        _cache ??= LoadCatalog();
        return _cache;
    }

    public SeedPlanDefinitionDto? GetByCaseId(string caseId) =>
        GetAll().FirstOrDefault(c => string.Equals(c.ScenarioId, caseId, StringComparison.OrdinalIgnoreCase));

    private IReadOnlyList<SeedPlanDefinitionDto> LoadCatalog()
    {
        if (!File.Exists(_catalogPath))
        {
            throw new FileNotFoundException($"Backend case catalog not found: {_catalogPath}");
        }

        var json = File.ReadAllText(_catalogPath);
        var entries = JsonSerializer.Deserialize<List<CaseCatalogEntry>>(json, JsonOptions)
                      ?? throw new InvalidOperationException("Backend case catalog is empty.");

        return entries
            .Select(entry => new SeedPlanDefinitionDto
            {
                ScenarioId = entry.CaseId,
                Title = entry.Title,
                Description = entry.Description,
                OutcomeTag = ParseOutcomeTag(entry.OutcomeTag),
                Context = entry.Context ?? new PlanContext()
            })
            .ToList();
    }

    private static ScenarioOutcomeTag ParseOutcomeTag(string? value) =>
        Enum.TryParse<ScenarioOutcomeTag>(value, true, out var parsed)
            ? parsed
            : ScenarioOutcomeTag.HealthyRun;

    private sealed class CaseCatalogEntry
    {
        public string CaseId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string? OutcomeTag { get; set; }

        public PlanContext? Context { get; set; }
    }
}
