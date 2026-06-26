using System.ComponentModel;
using CohereInventoryAndTrend.Mcp.Adapters;
using CohereInventoryAndTrend.Mcp.Models;
using ModelContextProtocol.Server;

namespace CohereInventoryAndTrend.Mcp.Tools;

public sealed class FeatureAndCausalityTools
{
    private static readonly string[] DriverCategories =
    [
        "price",
        "promotion",
        "seasonality",
        "inventory",
        "supplier"
    ];

    private readonly EvidenceIndexAdapter _evidenceIndexAdapter;
    private readonly LocalKnowledgeAdapter _localKnowledgeAdapter;
    private readonly PlanningDataAdapter _planningDataAdapter;

    public FeatureAndCausalityTools(
        EvidenceIndexAdapter evidenceIndexAdapter,
        LocalKnowledgeAdapter localKnowledgeAdapter,
        PlanningDataAdapter planningDataAdapter)
    {
        _evidenceIndexAdapter = evidenceIndexAdapter;
        _localKnowledgeAdapter = localKnowledgeAdapter;
        _planningDataAdapter = planningDataAdapter;
    }

    [McpServerTool]
    [Description("Returns the planning profile parsed from the local demo case dataset.")]
    public Task<GetPlanningProfileResponse> GetPlanningProfile(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default) =>
        _planningDataAdapter.GetPlanningProfileAsync(caseId, executionId, cancellationToken);

    [McpServerTool]
    [Description("Searches indexed planning signal evidence using Azure AI Search and Azure Foundry rerank.")]
    public Task<SearchSignalEvidenceResponse> SearchSignalEvidence(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default) =>
        SearchEvidenceAsync(caseId, executionId, cancellationToken);

    [McpServerTool]
    [Description("Returns compact grouped signal evidence for demand-driver categories.")]
    public async Task<GetDriverContextResponse> GetDriverContext(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        var categories = new List<DriverCategoryContext>();

        foreach (var category in DriverCategories)
        {
            var matches = await _evidenceIndexAdapter.SearchCategoryAsync(
                caseId,
                executionId,
                category,
                $"Summarize {category} drivers and elasticities for demand planning.",
                topK: 2,
                cancellationToken: cancellationToken);

            categories.Add(new DriverCategoryContext
            {
                Category = category,
                Matches = matches
            });
        }

        return new GetDriverContextResponse
        {
            CaseId = caseId,
            ExecutionId = executionId,
            Categories = categories
        };
    }

    [McpServerTool]
    [Description("Retrieves promotions and price-calendar knowledge from local knowledge files.")]
    public Task<GetRelevantKnowledgeResponse> GetRelevantPromotions(
        string caseId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        var query = DemoToolDefaults.CaseQuery(caseId, "Retrieve promotions and price calendar knowledge");
        return _localKnowledgeAdapter.GetRelevantKnowledgeAsync(
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
