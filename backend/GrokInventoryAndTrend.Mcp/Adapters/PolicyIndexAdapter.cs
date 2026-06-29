using System.Text.Json;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using GrokInventoryAndTrend.Mcp.Models;
using GrokInventoryAndTrend.Mcp.Options;
using Microsoft.Extensions.Options;

namespace GrokInventoryAndTrend.Mcp.Adapters;

public sealed class PolicyIndexAdapter
{
    public const string MetadataDocumentId = "policy-index-metadata";

    private readonly SearchClient _searchClient;
    private readonly FoundryEmbeddingService _embeddingService;
    private readonly FoundryRerankService _rerankService;
    private readonly AzureSearchOptions _options;

    public PolicyIndexAdapter(
        SearchIndexClient indexClient,
        FoundryEmbeddingService embeddingService,
        FoundryRerankService rerankService,
        IOptions<AzureSearchOptions> options)
    {
        _options = options.Value;
        _searchClient = indexClient.GetSearchClient(_options.PolicyIndexName);
        _embeddingService = embeddingService;
        _rerankService = rerankService;
    }

    public async Task<string?> GetStoredContentHashAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _searchClient.GetDocumentAsync<PolicyMetadataDocument>(
                MetadataDocumentId,
                new GetDocumentOptions { SelectedFields = { "contentHash" } },
                cancellationToken: cancellationToken);

            return string.IsNullOrWhiteSpace(response.Value.ContentHash)
                ? null
                : response.Value.ContentHash;
        }
        catch (Azure.RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SeedPoliciesAsync(
        IReadOnlyList<PolicyEntry> policies,
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        var documents = new List<PolicySearchDocument>();

        foreach (var policy in policies)
        {
            documents.Add(new PolicySearchDocument
            {
                Id = policy.PolicyRef,
                PolicyRef = policy.PolicyRef,
                DocumentType = "policy",
                Rule = policy.Rule,
                Threshold = policy.Threshold,
                Action = policy.Action,
                Exception = policy.Exception,
                FullText = policy.FullText,
                ContentHash = string.Empty
            });
        }

        documents.Add(new PolicySearchDocument
        {
            Id = MetadataDocumentId,
            PolicyRef = "METADATA",
            DocumentType = "metadata",
            Rule = string.Empty,
            Threshold = string.Empty,
            Action = string.Empty,
            Exception = string.Empty,
            FullText = "Policy index metadata",
            ContentHash = contentHash
        });

        var embeddings = await _embeddingService.EmbedAsync(
            documents.Select(document => document.FullText).ToArray(),
            cancellationToken);

        for (var index = 0; index < documents.Count; index++)
        {
            documents[index].Embedding = embeddings[index];
        }

        var batch = IndexDocumentsBatch.Upload(documents);
        await _searchClient.IndexDocumentsAsync(batch, cancellationToken: cancellationToken);
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

        var queryEmbedding = (await _embeddingService.EmbedAsync([effectiveQuery], cancellationToken)).Single();

        var searchOptions = new SearchOptions
        {
            Size = Math.Max(topK * 3, topK),
            Filter = "documentType eq 'policy'",
            Select = { "policyRef", "rule", "threshold", "action", "exception", "fullText" }
        };

        searchOptions.VectorSearch = new VectorSearchOptions
        {
            Queries =
            {
                new VectorizedQuery(queryEmbedding)
                {
                    KNearestNeighborsCount = searchOptions.Size,
                    Fields = { "embedding" }
                }
            }
        };

        var response = await _searchClient.SearchAsync<SearchDocument>(null, searchOptions, cancellationToken);
        var candidates = new List<PolicySearchCandidate>();

        await foreach (var result in response.Value.GetResultsAsync())
        {
            if (result.Document is null)
            {
                continue;
            }

            candidates.Add(new PolicySearchCandidate
            {
                PolicyRef = GetSearchDocumentString(result.Document, "policyRef"),
                Rule = GetSearchDocumentString(result.Document, "rule"),
                Threshold = GetSearchDocumentString(result.Document, "threshold"),
                Action = GetSearchDocumentString(result.Document, "action"),
                Exception = GetSearchDocumentString(result.Document, "exception"),
                FullText = GetSearchDocumentString(result.Document, "fullText")
            });
        }

        if (candidates.Count == 0)
        {
            return new GetRelevantPoliciesResponse
            {
                Query = query,
                Policies = []
            };
        }

        if (candidates.Count <= topK)
        {
            return new GetRelevantPoliciesResponse
            {
                Query = query,
                Policies = candidates.Select(candidate => ToPolicyMatch(candidate)).ToArray()
            };
        }

        var reranked = await _rerankService.RerankAsync(
            effectiveQuery,
            candidates.Select(candidate => candidate.FullText ?? string.Empty).ToArray(),
            topK,
            cancellationToken);

        var policies = reranked
            .Select(result => ToPolicyMatch(candidates[result.Index], result.Score))
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
            try
            {
                var response = await _searchClient.GetDocumentAsync<PolicySearchDocument>(
                    policyRef,
                    cancellationToken: cancellationToken);

                if (!string.Equals(response.Value.DocumentType, "policy", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                policies.Add(new PolicyMatch
                {
                    PolicyRef = response.Value.PolicyRef,
                    Rule = response.Value.Rule,
                    Threshold = response.Value.Threshold,
                    Action = response.Value.Action,
                    Exception = response.Value.Exception,
                    Score = 1
                });
            }
            catch (Azure.RequestFailedException exception) when (exception.Status == 404)
            {
            }
        }

        return new GetRelevantPoliciesResponse
        {
            Query = string.Join(",", policyRefs),
            Policies = policies
        };
    }

    private static PolicyMatch ToPolicyMatch(PolicySearchCandidate candidate, double score = 1) =>
        new()
        {
            PolicyRef = candidate.PolicyRef ?? string.Empty,
            Rule = candidate.Rule ?? string.Empty,
            Threshold = candidate.Threshold ?? string.Empty,
            Action = candidate.Action ?? string.Empty,
            Exception = candidate.Exception ?? string.Empty,
            Score = score
        };

    private static string? GetSearchDocumentString(SearchDocument document, string fieldName)
    {
        if (!document.TryGetValue(fieldName, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => value.ToString()
        };
    }

    private sealed class PolicySearchCandidate
    {
        public string? PolicyRef { get; init; }

        public string? Rule { get; init; }

        public string? Threshold { get; init; }

        public string? Action { get; init; }

        public string? Exception { get; init; }

        public string? FullText { get; init; }
    }

    private sealed class PolicyMetadataDocument
    {
        public string? ContentHash { get; set; }
    }

    public sealed class PolicySearchDocument
    {
        public required string Id { get; set; }

        public required string PolicyRef { get; set; }

        public required string DocumentType { get; set; }

        public required string Rule { get; set; }

        public required string Threshold { get; set; }

        public required string Action { get; set; }

        public required string Exception { get; set; }

        public required string FullText { get; set; }

        public required string ContentHash { get; set; }

        public IReadOnlyList<float> Embedding { get; set; } = [];
    }
}
