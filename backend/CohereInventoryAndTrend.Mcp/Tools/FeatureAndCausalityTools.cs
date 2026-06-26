using System.ComponentModel;
using CohereInventoryAndTrend.Mcp.Adapters;
using CohereInventoryAndTrend.Mcp.Models;
using ModelContextProtocol.Server;

namespace CohereInventoryAndTrend.Mcp.Tools;

public sealed class FeatureAndCausalityTools
{
    private readonly SignalEvidenceSearcher _signalEvidenceSearcher;
    private readonly LocalKnowledgeAdapter _localKnowledgeAdapter;
    private readonly PlanningDataAdapter _planningDataAdapter;

    public FeatureAndCausalityTools(
        SignalEvidenceSearcher signalEvidenceSearcher,
        LocalKnowledgeAdapter localKnowledgeAdapter,
        PlanningDataAdapter planningDataAdapter)
    {
        _signalEvidenceSearcher = signalEvidenceSearcher;
        _localKnowledgeAdapter = localKnowledgeAdapter;
        _planningDataAdapter = planningDataAdapter;
    }

    [McpServerTool]
    [Description("Returns the planning profile parsed from the case README user input.")]
    public Task<GetPlanningProfileResponse> GetPlanningProfile(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default) =>
        _planningDataAdapter.GetPlanningProfileAsync(caseId, executionId, cancellationToken);

    [McpServerTool]
    [Description("Searches planning signal evidence. Uses Azure AI Search when the inventory-signal-evidence index has matches; otherwise falls back to case fabric-pre-requisite-data.")]
    public Task<SearchSignalEvidenceResponse> SearchSignalEvidence(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default) =>
        SearchEvidenceAsync(caseId, executionId, cancellationToken);

    [McpServerTool]
    [Description("Returns compact grouped signal evidence for demand-driver categories from fabric-pre-requisite-data.")]
    public Task<GetDriverContextResponse> GetDriverContext(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default) =>
        _planningDataAdapter.GetDriverContextAsync(caseId, executionId, cancellationToken);

    [McpServerTool]
    [Description("Retrieves promotions and price-calendar knowledge from local knowledge files and case promotion events.")]
    public Task<GetRelevantKnowledgeResponse> GetRelevantPromotions(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        var query = DemoToolDefaults.CaseQuery(caseId, "Retrieve promotions and price calendar knowledge");
        return _localKnowledgeAdapter.GetPromotionKnowledgeAsync(
            query,
            DemoToolDefaults.CaseContext(caseId, executionId),
            DemoToolDefaults.DefaultTopK,
            cancellationToken);
    }

    private async Task<SearchSignalEvidenceResponse> SearchEvidenceAsync(
        string caseId,
        string executionId,
        CancellationToken cancellationToken)
    {
        var query = DemoToolDefaults.CaseQuery(caseId, "Retrieve demand driver signal evidence");
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
}
