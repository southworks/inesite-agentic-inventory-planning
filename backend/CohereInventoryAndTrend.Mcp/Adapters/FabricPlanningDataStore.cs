using Azure;
using CohereInventoryAndTrend.Mcp.Options;
using Microsoft.Extensions.Options;

namespace CohereInventoryAndTrend.Mcp.Adapters;

public sealed class FabricPlanningDataStore : IPlanningDataStore
{
    private readonly IFabricLakehouseClient _client;
    private readonly DatasetOptions _datasetOptions;
    private readonly string _evidenceRoot;

    public FabricPlanningDataStore(
        IFabricLakehouseClient client,
        IOptions<DataSourceOptions> dataSourceOptions,
        IOptions<DatasetOptions> datasetOptions)
    {
        _client = client;
        _datasetOptions = datasetOptions.Value;
        _evidenceRoot = string.IsNullOrWhiteSpace(dataSourceOptions.Value.FabricLakehouse?.EvidenceRoot)
            ? "Files/bronze"
            : dataSourceOptions.Value.FabricLakehouse!.EvidenceRoot;
    }

    public async Task<string> ReadDocumentAsync(string caseId, SignalCategory category, string fileName, CancellationToken cancellationToken = default)
    {
        ValidateCaseId(caseId);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name must be provided.", nameof(fileName));
        }

        var path = FilePath(caseId, category, fileName);
        try
        {
            return await _client.ReadFileAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException ex)
        {
            throw new FileNotFoundException($"Planning signal document not found: {path}", ex);
        }
    }

    public async Task<IReadOnlyList<string>> ListDocumentsAsync(string caseId, SignalCategory category, CancellationToken cancellationToken = default)
    {
        ValidateCaseId(caseId);
        var categoryPath = CategoryPath(caseId, category);

        IReadOnlyList<string> all;
        try
        {
            all = await _client.ListFilesAsync(categoryPath, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return [];
        }

        return all
            .Select(ExtractFileName)
            .Where(name => name is not null
                           && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                           && !name.StartsWith("SCHEMA", StringComparison.OrdinalIgnoreCase))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private string CategoryPath(string caseId, SignalCategory category)
    {
        var normalizedCaseId = CasePathResolver.NormalizeCaseId(caseId);
        return $"{_evidenceRoot}/{_datasetOptions.CasesRelativePath}/{normalizedCaseId}/{_datasetOptions.FabricPrerequisiteSubfolder}/{SignalCategoryFolders.For(category)}";
    }

    private string FilePath(string caseId, SignalCategory category, string fileName) =>
        $"{CategoryPath(caseId, category)}/{fileName}";

    private static string? ExtractFileName(string blobPath)
    {
        var lastSlash = blobPath.LastIndexOf('/');
        return lastSlash >= 0 ? blobPath[(lastSlash + 1)..] : blobPath;
    }

    private static void ValidateCaseId(string caseId)
    {
        if (string.IsNullOrWhiteSpace(caseId))
        {
            throw new ArgumentException("Case id must be provided.", nameof(caseId));
        }
    }
}
