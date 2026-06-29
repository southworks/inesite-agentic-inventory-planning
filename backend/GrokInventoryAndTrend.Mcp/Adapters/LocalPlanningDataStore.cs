using GrokInventoryAndTrend.Mcp.Options;
using Microsoft.Extensions.Options;

namespace GrokInventoryAndTrend.Mcp.Adapters;

public sealed class LocalPlanningDataStore : IPlanningDataStore
{
    private readonly string _datasetRootPath;
    private readonly DatasetOptions _datasetOptions;

    public LocalPlanningDataStore(IOptions<DatasetOptions> options, IHostEnvironment environment)
    {
        _datasetOptions = options.Value;
        _datasetRootPath = CasePathResolver.ResolveContentPath(environment.ContentRootPath, _datasetOptions.RootPath);
    }

    public async Task<string> ReadDocumentAsync(string caseId, SignalCategory category, string fileName, CancellationToken cancellationToken = default)
    {
        ValidateCaseId(caseId);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name must be provided.", nameof(fileName));
        }

        var path = FilePath(caseId, category, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Planning signal document not found: {path}", path);
        }

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    public Task<IReadOnlyList<string>> ListDocumentsAsync(string caseId, SignalCategory category, CancellationToken cancellationToken = default)
    {
        ValidateCaseId(caseId);
        var dir = CategoryDirectory(caseId, category);
        if (!Directory.Exists(dir))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var files = Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null && !name.StartsWith("SCHEMA", StringComparison.OrdinalIgnoreCase))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    private string CategoryDirectory(string caseId, SignalCategory category) =>
        CasePathResolver.GetCategoryDirectory(_datasetRootPath, _datasetOptions, caseId, category);

    private string FilePath(string caseId, SignalCategory category, string fileName) =>
        Path.Combine(CategoryDirectory(caseId, category), fileName);

    private static void ValidateCaseId(string caseId)
    {
        if (string.IsNullOrWhiteSpace(caseId))
        {
            throw new ArgumentException("Case id must be provided.", nameof(caseId));
        }
    }
}
