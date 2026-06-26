using System.Text;
using System.Text.Json;
using CohereInventoryAndTrend.Mcp.Models;

namespace CohereInventoryAndTrend.Mcp.Adapters;

public sealed partial class PlanningDataAdapter
{
    private static readonly IReadOnlyList<SignalCategory> Categories =
        Enum.GetValues<SignalCategory>();

    private readonly IPlanningDataStore _dataStore;

    public PlanningDataAdapter(IPlanningDataStore dataStore)
    {
        _dataStore = dataStore;
    }

    public async Task<GetPlanningSignalsResponse> GetPlanningSignalsAsync(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);

        var signals = new List<PlanningSignalDocument>();
        var availableCategories = new List<string>();

        foreach (var category in Categories)
        {
            IReadOnlyList<string> files;
            try
            {
                files = await _dataStore.ListDocumentsAsync(caseId, category, cancellationToken);
            }
            catch (KeyNotFoundException)
            {
                continue;
            }

            if (files.Count == 0)
            {
                continue;
            }

            var categoryName = category.ToString().ToLowerInvariant();
            availableCategories.Add(categoryName);

            foreach (var fileName in files.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                var content = await _dataStore.ReadDocumentAsync(caseId, category, fileName, cancellationToken);
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
                    SourcePath = fileName,
                    Content = root,
                    SummaryText = BuildSummaryText(categoryName, root)
                });
            }
        }

        var missingCategories = Categories
            .Select(category => category.ToString().ToLowerInvariant())
            .Except(availableCategories, StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new GetPlanningSignalsResponse
        {
            CaseId = caseId,
            ExecutionId = executionId,
            Signals = signals,
            AvailableCategories = availableCategories,
            MissingCategories = missingCategories
        };
    }

    public async Task<GetPlanningProfileResponse> GetPlanningProfileAsync(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);

        var signals = await GetPlanningSignalsAsync(caseId, executionId, cancellationToken);
        var planningSignals = signals.Signals
            .Where(signal => PlanningProfileParser.LooksLikePlanningRequestDocument(signal.SummaryText))
            .ToArray();

        if (planningSignals.Length == 0)
        {
            return new GetPlanningProfileResponse
            {
                CaseId = caseId,
                ExecutionId = executionId,
                Profile = new PlanningProfile(),
                Found = false
            };
        }

        var combinedText = string.Join(
            Environment.NewLine,
            planningSignals
                .Select(signal => signal.SummaryText)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Distinct(StringComparer.Ordinal));

        return new GetPlanningProfileResponse
        {
            CaseId = caseId,
            ExecutionId = executionId,
            Profile = PlanningProfileParser.Parse(combinedText),
            SourceDocumentId = planningSignals[0].DocumentId,
            Found = true
        };
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
