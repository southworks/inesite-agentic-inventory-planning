namespace CohereInventoryAndTrend.Mcp.Adapters;

public interface IPlanningDataStore
{
    Task<string> ReadDocumentAsync(string caseId, SignalCategory category, string fileName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListDocumentsAsync(string caseId, SignalCategory category, CancellationToken cancellationToken = default);
}
