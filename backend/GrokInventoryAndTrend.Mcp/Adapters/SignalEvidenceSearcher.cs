using GrokInventoryAndTrend.Mcp.Models;

namespace GrokInventoryAndTrend.Mcp.Adapters;

public sealed class SignalEvidenceSearcher
{
    private readonly PlanningDataAdapter _planningDataAdapter;

    public SignalEvidenceSearcher(PlanningDataAdapter planningDataAdapter) =>
        _planningDataAdapter = planningDataAdapter;

    public Task<IReadOnlyList<SignalMatch>> SearchAsync(
        string caseId,
        string executionId,
        string query,
        int topK,
        CancellationToken cancellationToken = default) =>
        _planningDataAdapter.SearchLocalSignalEvidenceAsync(
            caseId,
            executionId,
            query,
            topK,
            cancellationToken);
}
