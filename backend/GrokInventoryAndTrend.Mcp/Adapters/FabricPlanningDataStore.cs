using Azure;
using GrokInventoryAndTrend.Mcp.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GrokInventoryAndTrend.Mcp.Adapters;

public sealed class FabricPlanningDataStore : IPlanningDataStore
{
    private readonly IFabricLakehouseClient _client;
    private readonly DatasetOptions _datasetOptions;
    private readonly string _evidenceRoot;
    private readonly ILogger<FabricPlanningDataStore> _logger;

    public FabricPlanningDataStore(
        IFabricLakehouseClient client,
        IOptions<DataSourceOptions> dataSourceOptions,
        IOptions<DatasetOptions> datasetOptions,
        ILogger<FabricPlanningDataStore> logger)
    {
        _client = client;
        _datasetOptions = datasetOptions.Value;
        _logger = logger;
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
        var categoryFolder = SignalCategoryFolders.For(category);

        IReadOnlyList<string> all;
        try
        {
            all = await _client.ListFilesAsync(_evidenceRoot, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation("Evidence root {EvidenceRoot} not found (404).", _evidenceRoot);
            return [];
        }

        _logger.LogDebug("DFS listing at {EvidenceRoot} returned {Count} entries.",
            _evidenceRoot, all.Count);

        var inCase = all.Where(path => PathBelongsToCase(path, caseId)).ToList();
        var inCategory = inCase.Where(path => PathBelongsToCategory(path, categoryFolder)).ToList();

        var caseSkipped = all.Count - inCase.Count;
        var categorySkipped = inCase.Count - inCategory.Count;

        if (caseSkipped > 0 || categorySkipped > 0)
        {
            _logger.LogInformation(
                "Listing filter: {Total} total, {CaseSkipped} wrong case, {CategorySkipped} wrong category → {Kept} kept.",
                all.Count, caseSkipped, categorySkipped, inCategory.Count);
        }

        return inCategory
            .Select(ExtractFileName)
            .Where(name => name is not null
                           && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                           && !name.StartsWith("SCHEMA", StringComparison.OrdinalIgnoreCase))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private string CasePath(string caseId)
    {
        var normalizedCaseId = CasePathResolver.NormalizeCaseId(caseId);
        return $"{_evidenceRoot}/{_datasetOptions.CasesRelativePath}/{normalizedCaseId}/{_datasetOptions.FabricPrerequisiteSubfolder}";
    }

    private string CategoryPath(string caseId, SignalCategory category) =>
        $"{CasePath(caseId)}/{SignalCategoryFolders.For(category)}";

    private string FilePath(string caseId, SignalCategory category, string fileName) =>
        $"{CategoryPath(caseId, category)}/{fileName}";

    private bool PathBelongsToCase(string fullPath, string caseId)
    {
        var caseSegment = $"{CasePathResolver.NormalizeCaseId(caseId)}/";
        return fullPath.Contains($"/{_datasetOptions.CasesRelativePath}/{caseSegment}", StringComparison.Ordinal);
    }

    private static bool PathBelongsToCategory(string fullPath, string categoryFolder)
    {
        var lastSlash = fullPath.LastIndexOf('/');
        if (lastSlash < 0)
        {
            return false;
        }

        var parent = fullPath[..lastSlash];
        var parentLastSlash = parent.LastIndexOf('/');
        var parentFolder = parentLastSlash >= 0 ? parent[(parentLastSlash + 1)..] : parent;
        return string.Equals(parentFolder, categoryFolder, StringComparison.Ordinal);
    }

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
