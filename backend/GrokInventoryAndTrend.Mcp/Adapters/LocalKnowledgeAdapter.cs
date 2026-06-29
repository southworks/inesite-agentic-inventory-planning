using GrokInventoryAndTrend.Mcp.Models;
using GrokInventoryAndTrend.Mcp.Options;
using Microsoft.Extensions.Options;

namespace GrokInventoryAndTrend.Mcp.Adapters;

public sealed class LocalKnowledgeAdapter
{
    private readonly KnowledgeEntryParser _parser;
    private readonly PlanningDataAdapter _planningDataAdapter;
    private readonly DatasetOptions _datasetOptions;
    private readonly ILogger<LocalKnowledgeAdapter> _logger;
    private readonly Dictionary<KnowledgeScope, IReadOnlyList<KnowledgeEntry>> _cachedEntries = new();

    public LocalKnowledgeAdapter(
        KnowledgeEntryParser parser,
        PlanningDataAdapter planningDataAdapter,
        IOptions<DatasetOptions> datasetOptions,
        IHostEnvironment environment,
        ILogger<LocalKnowledgeAdapter> logger)
    {
        _parser = parser;
        _planningDataAdapter = planningDataAdapter;
        _datasetOptions = optionsWithResolvedPaths(options: datasetOptions.Value, environment.ContentRootPath);
        _logger = logger;
    }

    public Task<GetRelevantKnowledgeResponse> GetSignalQualityKnowledgeAsync(
        string query,
        string? caseContext = null,
        int topK = 5,
        CancellationToken cancellationToken = default) =>
        QueryKnowledgeAsync(query, caseContext, topK, KnowledgeScope.SignalQuality, includeCasePromotions: false, cancellationToken);

    public Task<GetRelevantKnowledgeResponse> GetTrendPatternKnowledgeAsync(
        string query,
        string? caseContext = null,
        int topK = 5,
        CancellationToken cancellationToken = default) =>
        QueryKnowledgeAsync(query, caseContext, topK, KnowledgeScope.TrendPatterns, includeCasePromotions: false, cancellationToken);

    public Task<GetRelevantKnowledgeResponse> GetPlanningConstraintKnowledgeAsync(
        string query,
        string? caseContext = null,
        int topK = 5,
        CancellationToken cancellationToken = default) =>
        QueryKnowledgeAsync(query, caseContext, topK, KnowledgeScope.PlanningConstraints, includeCasePromotions: false, cancellationToken);

    public Task<GetRelevantKnowledgeResponse> GetPromotionKnowledgeAsync(
        string query,
        string? caseContext = null,
        int topK = 5,
        CancellationToken cancellationToken = default) =>
        QueryKnowledgeAsync(query, caseContext, topK, KnowledgeScope.Promotions, includeCasePromotions: true, cancellationToken);

    private async Task<GetRelevantKnowledgeResponse> QueryKnowledgeAsync(
        string query,
        string? caseContext,
        int topK,
        KnowledgeScope scope,
        bool includeCasePromotions,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var entries = new List<KnowledgeEntry>(LoadScopedEntries(scope));
        if (includeCasePromotions && TryReadCaseId(caseContext, out var caseId))
        {
            var casePromotions = await _planningDataAdapter.GetCasePromotionKnowledgeAsync(caseId, cancellationToken);
            entries.AddRange(casePromotions);
        }

        var ranked = RankEntries(entries, query, caseContext)
            .Take(Math.Max(1, topK))
            .ToArray();

        return new GetRelevantKnowledgeResponse
        {
            Query = query,
            Entries = ranked
        };
    }

    private IReadOnlyList<KnowledgeEntry> LoadScopedEntries(KnowledgeScope scope)
    {
        if (_cachedEntries.TryGetValue(scope, out var cached))
        {
            return cached;
        }

        var path = scope switch
        {
            KnowledgeScope.SignalQuality => _datasetOptions.SignalQualityFilePath,
            KnowledgeScope.TrendPatterns => _datasetOptions.TrendPatternsFilePath,
            KnowledgeScope.PlanningConstraints => _datasetOptions.PlanningConstraintsFilePath,
            KnowledgeScope.Promotions => _datasetOptions.PromotionsFilePath,
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _logger.LogWarning("Local knowledge file for scope {Scope} was not found at {Path}.", scope, path);
            _cachedEntries[scope] = [];
            return _cachedEntries[scope];
        }

        _cachedEntries[scope] = _parser.Parse(File.ReadAllText(path));
        return _cachedEntries[scope];
    }

    private static IEnumerable<KnowledgeMatch> RankEntries(
        IReadOnlyList<KnowledgeEntry> entries,
        string query,
        string? caseContext)
    {
        var terms = query
            .Split([' ', ',', ';', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length > 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return entries
            .Select(entry =>
            {
                var score = ScoreEntry(entry, terms, caseContext);
                return new KnowledgeMatch
                {
                    KnowledgeRef = entry.KnowledgeRef,
                    Rule = entry.Rule,
                    Threshold = entry.Threshold,
                    Action = entry.Action,
                    Exception = entry.Exception,
                    Score = score
                };
            })
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.KnowledgeRef, StringComparer.OrdinalIgnoreCase);
    }

    private static double ScoreEntry(KnowledgeEntry entry, IReadOnlyList<string> terms, string? caseContext)
    {
        var haystack = string.Join(
            ' ',
            entry.KnowledgeRef,
            entry.Rule,
            entry.Threshold,
            entry.Action,
            entry.Exception,
            entry.FullText,
            caseContext ?? string.Empty);

        if (terms.Count == 0)
        {
            return 1;
        }

        return terms.Count(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryReadCaseId(string? caseContext, out string caseId)
    {
        caseId = string.Empty;
        if (string.IsNullOrWhiteSpace(caseContext))
        {
            return false;
        }

        foreach (var segment in caseContext.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!segment.StartsWith("caseId=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            caseId = segment["caseId=".Length..].Trim();
            return !string.IsNullOrWhiteSpace(caseId);
        }

        return false;
    }

    private static DatasetOptions optionsWithResolvedPaths(DatasetOptions options, string contentRootPath)
    {
        options.PromotionsFilePath = CasePathResolver.ResolveContentPath(contentRootPath, options.PromotionsFilePath);
        options.SignalQualityFilePath = CasePathResolver.ResolveContentPath(contentRootPath, options.SignalQualityFilePath);
        options.TrendPatternsFilePath = CasePathResolver.ResolveContentPath(contentRootPath, options.TrendPatternsFilePath);
        options.PlanningConstraintsFilePath = CasePathResolver.ResolveContentPath(contentRootPath, options.PlanningConstraintsFilePath);
        return options;
    }
}

internal enum KnowledgeScope
{
    SignalQuality,
    TrendPatterns,
    PlanningConstraints,
    Promotions
}
