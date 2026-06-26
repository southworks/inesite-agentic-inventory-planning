using System.ComponentModel;
using CohereInventoryAndTrend.Mcp.Adapters;
using CohereInventoryAndTrend.Mcp.Models;
using ModelContextProtocol.Server;

namespace CohereInventoryAndTrend.Mcp.Tools;

public sealed class SignalIngestionTools
{
    private readonly PlanningDataAdapter _planningDataAdapter;
    private readonly SignalEvidenceSearcher _signalEvidenceSearcher;
    private readonly LocalKnowledgeAdapter _localKnowledgeAdapter;

    public SignalIngestionTools(
        PlanningDataAdapter planningDataAdapter,
        SignalEvidenceSearcher signalEvidenceSearcher,
        LocalKnowledgeAdapter localKnowledgeAdapter)
    {
        _planningDataAdapter = planningDataAdapter;
        _signalEvidenceSearcher = signalEvidenceSearcher;
        _localKnowledgeAdapter = localKnowledgeAdapter;
    }

    [McpServerTool]
    [Description("Reads structured planning signals from POS, inventory, supplier, promotion, and data-entry sources.")]
    public Task<GetPlanningSignalsResponse> GetPlanningSignals(
        string caseId,
        string executionId,
        CancellationToken cancellationToken)
        => _planningDataAdapter.GetPlanningSignalsAsync(caseId, executionId, cancellationToken);

    [McpServerTool]
    [Description("Searches planning signal evidence. Uses Azure AI Search when the inventory-signal-evidence index has matches; otherwise falls back to case fabric-pre-requisite-data.")]
    public async Task<SearchSignalEvidenceResponse> SearchSignalEvidence(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        var query = DemoToolDefaults.CaseQuery(caseId, "Retrieve relevant signal evidence");
        var matches = await _signalEvidenceSearcher.SearchAsync(
            caseId,
            executionId,
            query,
            DemoToolDefaults.DefaultTopK,
            cancellationToken: cancellationToken);

        return new SearchSignalEvidenceResponse
        {
            CaseId = caseId,
            ExecutionId = executionId,
            Query = query,
            Matches = matches
        };
    }

    [McpServerTool]
    [Description("Retrieves signal quality rules, thresholds, and anomaly patterns from local knowledge files.")]
    public Task<GetRelevantKnowledgeResponse> GetSignalQualityRules(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        var query = DemoToolDefaults.CaseQuery(caseId, "Validate signal quality rules and thresholds");
        return _localKnowledgeAdapter.GetSignalQualityKnowledgeAsync(
            query,
            DemoToolDefaults.CaseContext(caseId, executionId),
            DemoToolDefaults.DefaultTopK,
            cancellationToken);
    }
}
