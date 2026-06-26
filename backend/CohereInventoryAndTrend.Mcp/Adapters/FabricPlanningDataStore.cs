using Azure;
using CohereInventoryAndTrend.Mcp.Options;
using Microsoft.Extensions.Options;

namespace CohereInventoryAndTrend.Mcp.Adapters;

public sealed class FabricPlanningDataStore : IPlanningDataStore
{
    private readonly IFabricLakehouseClient _client;
    private readonly string _evidenceRoot;

    public FabricPlanningDataStore(IFabricLakehouseClient client, IOptions<DataSourceOptions> options)
    {
        _client = client;
        _evidenceRoot = string.IsNullOrWhiteSpace(options.Value.FabricLakehouse?.EvidenceRoot)
            ? "Files/bronze"
            : options.Value.FabricLakehouse!.EvidenceRoot;
    }

    public async Task<string> ReadDocumentAsync(string caseId, SignalCategory category, string fileName, CancellationToken cancellationToken = default)
    {
        ValidatecaseId(caseId);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name must be provided.", nameof(fileName));
        }

        if (!fileName.StartsWith($"{caseId}_", StringComparison.Ordinal))
        {
            throw new FileNotFoundException(
                $"Planning signal document not found: file '{fileName}' does not belong to planning '{caseId}'.",
                fileName);
        }

        var path = FilePath(category, fileName);
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
        ValidatecaseId(caseId);
        var categoryFolder = SignalCategoryFolders.For(category);

        IReadOnlyList<string> all;
        try
        {
            all = await _client.ListFilesAsync(_evidenceRoot, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new KeyNotFoundException($"Planning signal root not found: {_evidenceRoot}", ex);
        }

        var prefix = $"{caseId}_";
        return all
            .Where(path => PathBelongsToCategory(path, categoryFolder)
                        && IsPlanningDocument(path, prefix))
            .Select(ExtractFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private string FilePath(SignalCategory category, string fileName) =>
        $"{_evidenceRoot}/{SignalCategoryFolders.For(category)}/{fileName}";

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

    private static bool IsPlanningDocument(string fullPath, string caseIdPrefix)
    {
        var fileName = ExtractFileName(fullPath);
        if (fileName is null)
        {
            return false;
        }

        if (fileName.StartsWith("SCHEMA", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return fileName.StartsWith(caseIdPrefix, StringComparison.Ordinal);
    }

    private static void ValidatecaseId(string caseId)
    {
        if (string.IsNullOrWhiteSpace(caseId))
        {
            throw new ArgumentException("Case id must be provided.", nameof(caseId));
        }
    }
}
