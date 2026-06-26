using CohereInventoryAndTrend.Mcp.Options;

namespace CohereInventoryAndTrend.Mcp.Adapters;

public static class CasePathResolver
{
    public static string NormalizeCaseId(string caseId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        return caseId.Trim();
    }

    public static string GetCaseDirectory(string datasetRootPath, DatasetOptions options, string caseId) =>
        Path.Combine(
            datasetRootPath,
            options.CasesRelativePath,
            NormalizeCaseId(caseId));

    public static string GetFabricPrerequisiteDirectory(string datasetRootPath, DatasetOptions options, string caseId) =>
        Path.Combine(
            GetCaseDirectory(datasetRootPath, options, caseId),
            options.FabricPrerequisiteSubfolder);

    public static string GetCategoryDirectory(
        string datasetRootPath,
        DatasetOptions options,
        string caseId,
        SignalCategory category) =>
        Path.Combine(
            GetFabricPrerequisiteDirectory(datasetRootPath, options, caseId),
            SignalCategoryFolders.For(category));

    public static string ResolveContentPath(string contentRootPath, string path)
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
