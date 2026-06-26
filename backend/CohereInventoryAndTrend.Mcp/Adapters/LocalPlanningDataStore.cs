using CohereInventoryAndTrend.Mcp.Options;
using Microsoft.Extensions.Options;

namespace CohereInventoryAndTrend.Mcp.Adapters;

public sealed class LocalPlanningDataStore : IPlanningDataStore
{
    private readonly string _rootPath;

    public LocalPlanningDataStore(IOptions<DatasetOptions> options, IHostEnvironment environment)
    {
        _rootPath = ResolveContentPath(environment.ContentRootPath, options.Value.RootPath);
    }

    public async Task<string> ReadDocumentAsync(string caseId, SignalCategory category, string fileName, CancellationToken cancellationToken = default)
    {
        ValidatecaseId(caseId);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name must be provided.", nameof(fileName));
        }

        var path = FilePath(category, fileName);
        if (!File.Exists(path) || !fileName.StartsWith($"{caseId}_", StringComparison.Ordinal))
        {
            throw new FileNotFoundException($"Planning signal document not found: {path}", path);
        }

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    public Task<IReadOnlyList<string>> ListDocumentsAsync(string caseId, SignalCategory category, CancellationToken cancellationToken = default)
    {
        ValidatecaseId(caseId);
        var dir = CategoryDirectory(category);
        if (!Directory.Exists(dir))
        {
            throw new KeyNotFoundException($"Planning signal category directory not found: {dir}");
        }

        var prefix = $"{caseId}_";
        var files = Directory.EnumerateFiles(dir, $"{prefix}*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null && !name.StartsWith("SCHEMA", StringComparison.OrdinalIgnoreCase))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    private string CategoryDirectory(SignalCategory category) =>
        Path.Combine(_rootPath, SignalCategoryFolders.For(category));

    private string FilePath(SignalCategory category, string fileName) =>
        Path.Combine(CategoryDirectory(category), fileName);

    private static void ValidatecaseId(string caseId)
    {
        if (string.IsNullOrWhiteSpace(caseId))
        {
            throw new ArgumentException("Case id must be provided.", nameof(caseId));
        }
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
