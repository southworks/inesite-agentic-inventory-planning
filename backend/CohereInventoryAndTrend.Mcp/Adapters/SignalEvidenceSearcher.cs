using CohereInventoryAndTrend.Mcp.Models;

namespace CohereInventoryAndTrend.Mcp.Adapters;

public sealed class SignalEvidenceSearcher
{
    private readonly EvidenceIndexAdapter _evidenceIndexAdapter;
    private readonly PlanningDataAdapter _planningDataAdapter;
    private readonly ILogger<SignalEvidenceSearcher> _logger;

    public SignalEvidenceSearcher(
        EvidenceIndexAdapter evidenceIndexAdapter,
        PlanningDataAdapter planningDataAdapter,
        ILogger<SignalEvidenceSearcher> logger)
    {
        _evidenceIndexAdapter = evidenceIndexAdapter;
        _planningDataAdapter = planningDataAdapter;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SignalMatch>> SearchAsync(
        string caseId,
        string executionId,
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var indexedMatches = await _evidenceIndexAdapter.SearchAsync(
                caseId,
                executionId,
                query,
                topK,
                cancellationToken: cancellationToken);

            if (indexedMatches.Count > 0)
            {
                return indexedMatches;
            }

            _logger.LogInformation(
                "Azure AI Search index returned no matches for case {CaseId}; falling back to fabric-pre-requisite-data.",
                caseId);
        }
        catch (Exception exception) when (exception is HttpRequestException or Azure.RequestFailedException)
        {
            _logger.LogWarning(
                exception,
                "Azure AI Search lookup failed for case {CaseId}; falling back to fabric-pre-requisite-data.",
                caseId);
        }

        return await _planningDataAdapter.SearchLocalSignalEvidenceAsync(
            caseId,
            executionId,
            query,
            topK,
            cancellationToken);
    }
}
