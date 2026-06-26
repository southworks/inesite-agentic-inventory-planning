using System.ComponentModel;
using CohereInventoryAndTrend.Mcp.Adapters;
using CohereInventoryAndTrend.Mcp.Models;
using ModelContextProtocol.Server;

namespace CohereInventoryAndTrend.Mcp.Tools;

public sealed class ForecastingTools
{
    private static readonly string[] ForecastingCategories =
    [
        "pos_transactions",
        "inventory",
        "promotionsprice",
        "trend"
    ];

    private readonly EvidenceIndexAdapter _evidenceIndexAdapter;
    private readonly LocalKnowledgeAdapter _localKnowledgeAdapter;

    public ForecastingTools(
        EvidenceIndexAdapter evidenceIndexAdapter,
        LocalKnowledgeAdapter localKnowledgeAdapter)
    {
        _evidenceIndexAdapter = evidenceIndexAdapter;
        _localKnowledgeAdapter = localKnowledgeAdapter;
    }

    [McpServerTool]
    [Description("Searches indexed planning signal evidence for short-term demand forecasting.")]
    public Task<SearchSignalEvidenceResponse> SearchSignalEvidence(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default) =>
        SearchEvidenceAsync(caseId, executionId, cancellationToken);

    [McpServerTool]
    [Description("Retrieves RAG-indexed short-term trend patterns and demand-shift signals.")]
    public Task<GetRelevantKnowledgeResponse> GetTrendPatterns(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        var query = DemoToolDefaults.CaseQuery(caseId, "Retrieve short-term trend patterns and demand shifts");
        return _localKnowledgeAdapter.GetRelevantKnowledgeAsync(
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
        return _localKnowledgeAdapter.GetRelevantKnowledgeAsync(
            query,
            DemoToolDefaults.CaseContext(caseId, executionId),
            DemoToolDefaults.DefaultTopK,
            cancellationToken);
    }

    [McpServerTool]
    [Description("Returns compact grouped signal evidence for forecasting categories.")]
    public async Task<GetForecastingContextResponse> GetForecastingContext(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        var categories = new List<DriverCategoryContext>();

        foreach (var category in ForecastingCategories)
        {
            var matches = await _evidenceIndexAdapter.SearchCategoryAsync(
                caseId,
                executionId,
                category,
                $"Summarize {category} evidence for short-term demand forecasting.",
                topK: 2,
                cancellationToken: cancellationToken);

            categories.Add(new DriverCategoryContext
            {
                Category = category,
                Matches = matches
            });
        }

        return new GetForecastingContextResponse
        {
            CaseId = caseId,
            ExecutionId = executionId,
            Categories = categories
        };
    }

    private async Task<SearchSignalEvidenceResponse> SearchEvidenceAsync(
        string caseId,
        string executionId,
        CancellationToken cancellationToken)
    {
        var query = DemoToolDefaults.CaseQuery(caseId, "Retrieve forecasting signal evidence");
        var matches = await _evidenceIndexAdapter.SearchAsync(
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
