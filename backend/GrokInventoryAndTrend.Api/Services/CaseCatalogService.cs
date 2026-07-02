using System.Text.Json;
using System.Text.RegularExpressions;
using GrokInventoryAndTrend.Api.Contracts;
using GrokInventoryAndTrend.Api.Options;
using Microsoft.Extensions.Options;

namespace GrokInventoryAndTrend.Api.Services;

public sealed partial class CaseCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly DatasetOptions _options;
    private readonly ILogger<CaseCatalogService> _logger;
    private IReadOnlyList<CaseSummaryResponse>? _cache;

    public CaseCatalogService(
        IOptions<DatasetOptions> options,
        ILogger<CaseCatalogService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public IReadOnlyList<CaseSummaryResponse> GetAllCases()
    {
        _cache ??= LoadCatalog();
        return _cache;
    }

    private IReadOnlyList<CaseSummaryResponse> LoadCatalog()
    {
        string catalogPath = Path.Combine(
            _options.RootPath,
            _options.CasesRelativePath,
            "catalog.json");

        if (!File.Exists(catalogPath))
        {
            throw new FileNotFoundException($"Case catalog not found: {catalogPath}");
        }

        string json = File.ReadAllText(catalogPath);
        List<CaseCatalogEntry> entries = JsonSerializer.Deserialize<List<CaseCatalogEntry>>(json, JsonOptions)
                                         ?? throw new InvalidOperationException("Case catalog is empty.");

        var supportedCaseIds = LocalDocumentStorageService.GetSupportedCaseIds();

        var cases = entries
            .Where(entry => supportedCaseIds.Contains(entry.CaseId))
            .Select(MapToSummary)
            .ToList();

        _logger.LogInformation("Loaded {CaseCount} case(s) from catalog {CatalogPath}.", cases.Count, catalogPath);

        return cases;
    }

    private CaseSummaryResponse MapToSummary(CaseCatalogEntry entry)
    {
        string caseFolderPath = Path.Combine(
            _options.RootPath,
            _options.CasesRelativePath,
            entry.CaseId);

        return new CaseSummaryResponse
        {
            CaseId = entry.CaseId,
            Title = entry.Title,
            Description = entry.Description,
            OutcomeTag = entry.OutcomeTag ?? "HealthyRun",
            LegacyId = entry.LegacyId,
            ExpectedOutcome = ReadExpectedOutcome(caseFolderPath) ?? string.Empty,
            Context = MapContext(entry.Context)
        };
    }

    private static CaseContextResponse MapContext(CaseCatalogContext? context) =>
        new()
        {
            Category = context?.Category ?? string.Empty,
            Campaign = context?.Campaign ?? string.Empty,
            PlanningHorizon = context?.PlanningHorizon ?? string.Empty,
            BudgetCap = context?.BudgetCap ?? 0,
            TargetFillRate = context?.TargetFillRate ?? 0,
            AffectedSkuCount = context?.AffectedSkuCount ?? 0,
            SignalSources = context?.SignalSources ?? []
        };

    private static string? ReadExpectedOutcome(string caseFolderPath)
    {
        string readmePath = Path.Combine(caseFolderPath, "README.md");
        if (!File.Exists(readmePath))
        {
            return null;
        }

        string text = File.ReadAllText(readmePath);
        Match match = ExpectedOutcomePattern().Match(text);
        return match.Success ? match.Groups["content"].Value.Trim() : null;
    }

    [GeneratedRegex(@"\*\*Expected outcome:\*\*\s*(?<content>.+)", RegexOptions.IgnoreCase)]
    private static partial Regex ExpectedOutcomePattern();

    private sealed class CaseCatalogEntry
    {
        public string CaseId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string? OutcomeTag { get; set; }

        public string? LegacyId { get; set; }

        public CaseCatalogContext? Context { get; set; }
    }

    private sealed class CaseCatalogContext
    {
        public string? Category { get; set; }

        public string? Campaign { get; set; }

        public string? PlanningHorizon { get; set; }

        public decimal BudgetCap { get; set; }

        public decimal TargetFillRate { get; set; }

        public int AffectedSkuCount { get; set; }

        public List<string>? SignalSources { get; set; }
    }
}
