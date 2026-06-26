using System.ComponentModel;
using CohereInventoryAndTrend.Mcp.Adapters;
using CohereInventoryAndTrend.Mcp.Models;
using ModelContextProtocol.Server;

namespace CohereInventoryAndTrend.Mcp.Tools;

public sealed class ForecastingTools
{
    private readonly SignalEvidenceSearcher _signalEvidenceSearcher;
    private readonly LocalKnowledgeAdapter _localKnowledgeAdapter;
    private readonly PlanningDataAdapter _planningDataAdapter;

    public ForecastingTools(
        SignalEvidenceSearcher signalEvidenceSearcher,
        LocalKnowledgeAdapter localKnowledgeAdapter,
        PlanningDataAdapter planningDataAdapter)
    {
        _signalEvidenceSearcher = signalEvidenceSearcher;
        _localKnowledgeAdapter = localKnowledgeAdapter;
        _planningDataAdapter = planningDataAdapter;
    }

    [McpServerTool]
    [Description("Searches planning signal evidence. Uses Azure AI Search when the inventory-signal-evidence index has matches; otherwise falls back to case fabric-pre-requisite-data.")]
    public Task<SearchSignalEvidenceResponse> SearchSignalEvidence(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default) =>
        SearchEvidenceAsync(caseId, executionId, cancellationToken);

    [McpServerTool]
    [Description("Retrieves short-term trend patterns and demand-shift knowledge.")]
    public Task<GetRelevantKnowledgeResponse> GetTrendPatterns(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        var query = DemoToolDefaults.CaseQuery(caseId, "Retrieve short-term trend patterns and demand shifts");
        return _localKnowledgeAdapter.GetTrendPatternKnowledgeAsync(
            query,
            DemoToolDefaults.CaseContext(caseId, executionId),
            DemoToolDefaults.DefaultTopK,
            cancellationToken);
    }

    [McpServerTool]
    [Description("Retrieves promotions and price-calendar knowledge relevant to forecast adjustments.")]
    public Task<GetRelevantKnowledgeResponse> GetRelevantPromotions(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        var query = DemoToolDefaults.CaseQuery(caseId, "Retrieve promotion and price impacts for forecasting");
        return _localKnowledgeAdapter.GetPromotionKnowledgeAsync(
            query,
            DemoToolDefaults.CaseContext(caseId, executionId),
            DemoToolDefaults.DefaultTopK,
            cancellationToken);
    }

    [McpServerTool]
    [Description("Returns compact grouped signal evidence for forecasting categories from fabric-pre-requisite-data.")]
    public Task<GetForecastingContextResponse> GetForecastingContext(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default) =>
        _planningDataAdapter.GetForecastingContextAsync(caseId, executionId, cancellationToken);

    private async Task<SearchSignalEvidenceResponse> SearchEvidenceAsync(
        string caseId,
        string executionId,
        CancellationToken cancellationToken)
    {
        var query = DemoToolDefaults.CaseQuery(caseId, "Retrieve forecasting signal evidence");
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
