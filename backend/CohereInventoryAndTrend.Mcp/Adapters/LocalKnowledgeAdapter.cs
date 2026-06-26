using CohereInventoryAndTrend.Mcp.Models;
using CohereInventoryAndTrend.Mcp.Options;
using Microsoft.Extensions.Options;

namespace CohereInventoryAndTrend.Mcp.Adapters;

public sealed class LocalKnowledgeAdapter
{
    private readonly KnowledgeEntryParser _parser;
    private readonly DatasetOptions _datasetOptions;
    private readonly ILogger<LocalKnowledgeAdapter> _logger;
    private IReadOnlyList<KnowledgeEntry>? _cachedEntries;

    public LocalKnowledgeAdapter(
        KnowledgeEntryParser parser,
        IOptions<DatasetOptions> datasetOptions,
        IHostEnvironment environment,
        ILogger<LocalKnowledgeAdapter> logger)
    {
        _parser = parser;
        _datasetOptions = optionsWithResolvedPaths(options: datasetOptions.Value, environment.ContentRootPath);
        _logger = logger;
    }

    public Task<GetRelevantKnowledgeResponse> GetRelevantKnowledgeAsync(
        string query,
        string? caseContext = null,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var entries = LoadEntries();
        var ranked = RankEntries(entries, query, caseContext)
            .Take(Math.Max(1, topK))
            .ToArray();

        return Task.FromResult(new GetRelevantKnowledgeResponse
        {
            Query = query,
            Entries = ranked
        });
    }

    private IReadOnlyList<KnowledgeEntry> LoadEntries()
    {
        if (_cachedEntries is not null)
        {
            return _cachedEntries;
        }

        var paths = new[]
        {
            _datasetOptions.SignalQualityFilePath,
            _datasetOptions.PromotionsFilePath,
            _datasetOptions.TrendPatternsFilePath,
            _datasetOptions.PlanningConstraintsFilePath
        };

        var builder = new System.Text.StringBuilder();
        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (!File.Exists(path))
            {
                _logger.LogWarning("Local knowledge file not found at {Path}. Skipping.", path);
                continue;
            }

            builder.AppendLine(File.ReadAllText(path));
            builder.AppendLine();
        }

        _cachedEntries = _parser.Parse(builder.ToString());
        return _cachedEntries;
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

    private static DatasetOptions optionsWithResolvedPaths(DatasetOptions options, string contentRootPath)
    {
        options.PromotionsFilePath = ResolveContentPath(contentRootPath, options.PromotionsFilePath);
        options.SignalQualityFilePath = ResolveContentPath(contentRootPath, options.SignalQualityFilePath);
        options.TrendPatternsFilePath = ResolveContentPath(contentRootPath, options.TrendPatternsFilePath);
        options.PlanningConstraintsFilePath = ResolveContentPath(contentRootPath, options.PlanningConstraintsFilePath);
        return options;
    }

    private static string ResolveContentPath(string contentRootPath, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(contentRootPath, path));
    }
}
