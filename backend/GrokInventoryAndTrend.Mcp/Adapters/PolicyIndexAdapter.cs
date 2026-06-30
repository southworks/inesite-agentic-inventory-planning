using GrokInventoryAndTrend.Mcp.Models;

namespace GrokInventoryAndTrend.Mcp.Adapters;

public sealed class PolicyIndexAdapter
{
    private readonly FoundryIqRetrievalService _foundryIqRetrievalService;
    private readonly PolicyParser _policyParser;

    public PolicyIndexAdapter(
        FoundryIqRetrievalService foundryIqRetrievalService,
        PolicyParser policyParser)
    {
        _foundryIqRetrievalService = foundryIqRetrievalService;
        _policyParser = policyParser;
    }

    public async Task<GetRelevantPoliciesResponse> GetRelevantPoliciesAsync(
        string query,
        string? caseContext,
        int topK,
        CancellationToken cancellationToken = default)
    {
        var effectiveQuery = string.IsNullOrWhiteSpace(caseContext)
            ? query
            : $"{query}\n\nCase context:\n{caseContext}";

        var documents = await _foundryIqRetrievalService.RetrievePoliciesAsync(
            effectiveQuery,
            Math.Max(topK * 3, topK),
            cancellationToken);

        var policies = documents
            .Select(ParsePolicyDocument)
            .Where(policy => policy is not null)
            .Select(policy => policy!)
            .DistinctBy(policy => policy.PolicyRef, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, topK))
            .ToArray();

        return new GetRelevantPoliciesResponse
        {
            Query = query,
            Policies = policies
        };
    }

    public async Task<GetRelevantPoliciesResponse> GetPoliciesByRefsAsync(
        IReadOnlyList<string> policyRefs,
        CancellationToken cancellationToken = default)
    {
        var policies = new List<PolicyMatch>();

        foreach (string policyRef in policyRefs
                     .Where(reference => !string.IsNullOrWhiteSpace(reference))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var documents = await _foundryIqRetrievalService.RetrievePolicyByRefAsync(
                policyRef,
                cancellationToken);

            var match = documents
                .Select(ParsePolicyDocument)
                .FirstOrDefault(policy => policy is not null
                    && string.Equals(policy.PolicyRef, policyRef, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                policies.Add(match);
            }
        }

        return new GetRelevantPoliciesResponse
        {
            Query = string.Join(",", policyRefs),
            Policies = policies
        };
    }

    private PolicyMatch? ParsePolicyDocument(FoundryIqDocument document)
    {
        var entries = _policyParser.Parse(document.Content);
        if (entries.Count == 0)
        {
            return null;
        }

        var entry = entries[0];
        return new PolicyMatch
        {
            PolicyRef = entry.PolicyRef,
            Rule = entry.Rule,
            Threshold = entry.Threshold,
            Action = entry.Action,
            Exception = entry.Exception,
            Score = document.Score
        };
    }
}
