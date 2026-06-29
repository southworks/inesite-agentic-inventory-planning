using System.Text;
using System.Text.Json;
using GrokInventoryAndTrend.Mcp.Models;
using GrokInventoryAndTrend.Mcp.Options;
using Microsoft.Extensions.Options;

namespace GrokInventoryAndTrend.Mcp.Adapters;

public sealed partial class PlanningDataAdapter
{
    private static readonly IReadOnlyList<SignalCategory> AllCategories =
        Enum.GetValues<SignalCategory>();

    private static readonly IReadOnlyList<SignalCategory> ReplenishmentCategories =
    [
        SignalCategory.Inventory,
        SignalCategory.SupplierData
    ];

    private static readonly IReadOnlyList<SignalCategory> ForecastingCategories =
    [
        SignalCategory.PosTransactions,
        SignalCategory.Inventory,
        SignalCategory.PromotionsPrice
    ];

    private readonly IPlanningDataStore _dataStore;
    private readonly string _datasetRootPath;
    private readonly DatasetOptions _datasetOptions;

    public PlanningDataAdapter(
        IPlanningDataStore dataStore,
        IOptions<DatasetOptions> datasetOptions,
        IHostEnvironment environment)
    {
        _dataStore = dataStore;
        _datasetOptions = datasetOptions.Value;
        _datasetRootPath = CasePathResolver.ResolveContentPath(environment.ContentRootPath, _datasetOptions.RootPath);
    }

    public Task<GetPlanningSignalsResponse> GetPlanningSignalsAsync(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default) =>
        GetSignalsAsync(caseId, executionId, AllCategories, cancellationToken);

    public Task<GetPlanningSignalsResponse> GetReplenishmentSignalsAsync(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default) =>
        GetSignalsAsync(caseId, executionId, ReplenishmentCategories, cancellationToken);

    public Task<GetPlanningSignalsResponse> GetForecastingSignalsAsync(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default) =>
        GetSignalsAsync(caseId, executionId, ForecastingCategories, cancellationToken);

    public async Task<IReadOnlyList<KnowledgeEntry>> GetCasePromotionKnowledgeAsync(
        string caseId,
        CancellationToken cancellationToken = default)
    {
        var response = await GetSignalsAsync(
            caseId,
            executionId: string.Empty,
            [SignalCategory.PromotionsPrice],
            cancellationToken);

        return response.Signals
            .Select(ToPromotionKnowledgeEntry)
            .ToArray();
    }

    public async Task<GetDriverContextResponse> GetDriverContextAsync(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        var categoryMappings = new (string Name, SignalCategory[] Categories)[]
        {
            ("price", [SignalCategory.PosTransactions]),
            ("promotion", [SignalCategory.PromotionsPrice]),
            ("seasonality", [SignalCategory.PosTransactions]),
            ("inventory", [SignalCategory.Inventory]),
            ("supplier", [SignalCategory.SupplierData])
        };

        return await BuildGroupedContextAsync<GetDriverContextResponse>(
            caseId,
            executionId,
            categoryMappings,
            (normalizedCaseId, normalizedExecutionId, categories) => new GetDriverContextResponse
            {
                CaseId = normalizedCaseId,
                ExecutionId = normalizedExecutionId,
                Categories = categories
            },
            cancellationToken);
    }

    public Task<GetForecastingContextResponse> GetForecastingContextAsync(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        var categoryMappings = new (string Name, SignalCategory[] Categories)[]
        {
            ("pos_transactions", [SignalCategory.PosTransactions]),
            ("inventory", [SignalCategory.Inventory]),
            ("promotionsprice", [SignalCategory.PromotionsPrice]),
            ("trend", [SignalCategory.PosTransactions])
        };

        return BuildGroupedContextAsync<GetForecastingContextResponse>(
            caseId,
            executionId,
            categoryMappings,
            (normalizedCaseId, normalizedExecutionId, categories) => new GetForecastingContextResponse
            {
                CaseId = normalizedCaseId,
                ExecutionId = normalizedExecutionId,
                Categories = categories
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<SignalMatch>> SearchLocalSignalEvidenceAsync(
        string caseId,
        string executionId,
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var response = await GetPlanningSignalsAsync(caseId, executionId, cancellationToken);
        var terms = query
            .Split([' ', ',', ';', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length > 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return response.Signals
            .Select(signal => new
            {
                Signal = signal,
                Score = ScoreSignal(signal, terms, caseId)
            })
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Signal.DocumentId, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, topK))
            .Select(result => new SignalMatch
            {
                DocumentId = result.Signal.DocumentId,
                DocumentType = result.Signal.DocumentType,
                Category = result.Signal.Category,
                Snippet = TruncateSnippet(result.Signal.SummaryText),
                Score = result.Score
            })
            .ToArray();
    }

    public async Task<GetPlanningProfileResponse> GetPlanningProfileAsync(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);

        var caseDirectory = CasePathResolver.GetCaseDirectory(_datasetRootPath, _datasetOptions, caseId);
        var userInput = CaseReadmeReader.ReadUserInput(caseDirectory);
        if (string.IsNullOrWhiteSpace(userInput))
        {
            return new GetPlanningProfileResponse
            {
                CaseId = caseId,
                ExecutionId = executionId,
                Profile = new PlanningProfile(),
                Found = false
            };
        }

        var summaryText = CaseReadmeReader.BuildPlanningRequestSummary(userInput);
        return new GetPlanningProfileResponse
        {
            CaseId = caseId,
            ExecutionId = executionId,
            Profile = PlanningProfileParser.Parse(summaryText),
            SourceDocumentId = Path.Combine(caseDirectory, "README.md"),
            Found = true
        };
    }

    private async Task<GetPlanningSignalsResponse> GetSignalsAsync(
        string caseId,
        string executionId,
        IReadOnlyList<SignalCategory> categories,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);

        var normalizedCaseId = CasePathResolver.NormalizeCaseId(caseId);
        var signals = new List<PlanningSignalDocument>();
        var availableCategories = new List<string>();

        foreach (var category in categories)
        {
            var files = await _dataStore.ListDocumentsAsync(normalizedCaseId, category, cancellationToken);
            if (files.Count == 0)
            {
                continue;
            }

            var categoryName = SignalCategoryFolders.CategoryName(category);
            availableCategories.Add(categoryName);

            foreach (var fileName in files.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                var content = await _dataStore.ReadDocumentAsync(normalizedCaseId, category, fileName, cancellationToken);
                var jsonText = content.TrimStart('\uFEFF');
                using var document = JsonDocument.Parse(jsonText);
                var root = document.RootElement.Clone();

                signals.Add(new PlanningSignalDocument
                {
                    DocumentId = root.TryGetProperty("document_id", out var documentId)
                        ? documentId.GetString() ?? Path.GetFileNameWithoutExtension(fileName)
                        : Path.GetFileNameWithoutExtension(fileName),
                    DocumentType = root.TryGetProperty("document_type", out var documentType)
                        ? documentType.GetString() ?? categoryName
                        : categoryName,
                    Category = categoryName,
                    SourcePath = Path.Combine(
                        _datasetOptions.FabricPrerequisiteSubfolder,
                        categoryName,
                        fileName),
                    Content = root,
                    SummaryText = BuildSummaryText(categoryName, root)
                });
            }
        }

        var missingCategories = categories
            .Select(SignalCategoryFolders.CategoryName)
            .Except(availableCategories, StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new GetPlanningSignalsResponse
        {
            CaseId = normalizedCaseId,
            ExecutionId = executionId,
            Signals = signals,
            AvailableCategories = availableCategories,
            MissingCategories = missingCategories,
            Source = _datasetOptions.FabricPrerequisiteSubfolder
        };
    }

    private async Task<TResponse> BuildGroupedContextAsync<TResponse>(
        string caseId,
        string executionId,
        IReadOnlyList<(string Name, SignalCategory[] Categories)> categoryMappings,
        Func<string, string, IReadOnlyList<DriverCategoryContext>, TResponse> createResponse,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);

        var normalizedCaseId = CasePathResolver.NormalizeCaseId(caseId);
        var categories = new List<DriverCategoryContext>();

        foreach (var (name, signalCategories) in categoryMappings)
        {
            var response = await GetSignalsAsync(
                normalizedCaseId,
                executionId,
                signalCategories,
                cancellationToken);

            categories.Add(new DriverCategoryContext
            {
                Category = name,
                Matches = response.Signals
                    .OrderByDescending(signal => signal.SourcePath, StringComparer.OrdinalIgnoreCase)
                    .Take(2)
                    .Select(ToSignalMatch)
                    .ToArray()
            });
        }

        return createResponse(normalizedCaseId, executionId, categories);
    }

    private static SignalMatch ToSignalMatch(PlanningSignalDocument signal) =>
        new()
        {
            DocumentId = signal.DocumentId,
            DocumentType = signal.DocumentType,
            Category = signal.Category,
            Snippet = TruncateSnippet(signal.SummaryText),
            Score = 1
        };

    private static string TruncateSnippet(string? text)
    {
        const int maxLength = ToolResponseLimits.MaxEvidenceSnippetLength;
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text ?? string.Empty;
        }

        return text[..maxLength] + "...";
    }

    private static double ScoreSignal(PlanningSignalDocument signal, IReadOnlyList<string> terms, string caseId)
    {
        var haystack = string.Join(
            ' ',
            signal.DocumentId,
            signal.DocumentType,
            signal.Category,
            signal.SourcePath,
            signal.SummaryText,
            caseId);

        if (terms.Count == 0)
        {
            return 1;
        }

        return terms.Count(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static KnowledgeEntry ToPromotionKnowledgeEntry(PlanningSignalDocument signal)
    {
        var builder = new StringBuilder(signal.SummaryText);
        return new KnowledgeEntry
        {
            KnowledgeRef = signal.DocumentId,
            Rule = $"Promotion event for case-scoped fabric prerequisite data ({signal.DocumentType}).",
            Threshold = ReadProperty(signal.Content, "discount_pct", "expected_uplift_pct"),
            Action = ReadProperty(signal.Content, "start_date", "end_date", "observed_uplift_pct"),
            Exception = string.Empty,
            FullText = builder.ToString()
        };
    }

    private static string ReadProperty(JsonElement content, params string[] propertyNames)
    {
        var values = propertyNames
            .Select(name => content.TryGetProperty(name, out var value) ? $"{name}={value}" : null)
            .Where(value => value is not null)
            .ToArray();

        return values.Length == 0 ? string.Empty : string.Join("; ", values!);
    }

    public static string BuildSummaryText(string category, JsonElement content)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Category: {category}");

        foreach (var property in content.EnumerateObject().OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
            {
                builder.AppendLine($"{property.Name}: {property.Value.GetRawText()}");
            }
            else
            {
                builder.AppendLine($"{property.Name}: {property.Value}");
            }
        }

        return builder.ToString().Trim();
    }
}
