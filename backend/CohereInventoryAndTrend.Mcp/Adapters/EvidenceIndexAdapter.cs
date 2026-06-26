using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using System.Text.Json;
using CohereInventoryAndTrend.Mcp.Models;
using CohereInventoryAndTrend.Mcp.Options;
using Microsoft.Extensions.Options;

namespace CohereInventoryAndTrend.Mcp.Adapters;

public sealed class EvidenceIndexAdapter
{
    private const string MetadataDocumentType = "metadata";
    private const int MaxSearchQueryLength = 4096;
    private const int MaxEvidenceSnippetLength = ToolResponseLimits.MaxEvidenceSnippetLength;

    private readonly SearchClient _searchClient;
    private readonly FoundryEmbeddingService _embeddingService;
    private readonly FoundryRerankService _rerankService;

    public EvidenceIndexAdapter(
        SearchIndexClient indexClient,
        FoundryEmbeddingService embeddingService,
        FoundryRerankService rerankService,
        IOptions<AzureSearchOptions> options)
    {
        var searchOptions = options.Value;
        _searchClient = indexClient.GetSearchClient(searchOptions.EvidenceIndexName);
        _embeddingService = embeddingService;
        _rerankService = rerankService;
    }

    public async Task<IReadOnlyList<SignalMatch>> SearchAsync(
        string caseId,
        string executionId,
        string query,
        int topK,
        string? sourceType = null,
        CancellationToken cancellationToken = default)
    {
        var candidates = await SearchCandidatesAsync(
            caseId,
            executionId,
            query,
            category: null,
            sourceType,
            candidateSize: Math.Max(topK * 3, topK),
            cancellationToken);

        return await RerankEvidenceAsync(query, candidates, topK, cancellationToken);
    }

    public async Task<IReadOnlyList<SignalMatch>> SearchCategoryAsync(
        string caseId,
        string executionId,
        string category,
        string query,
        int topK,
        string? sourceType = null,
        CancellationToken cancellationToken = default)
    {
        var candidates = await SearchCandidatesAsync(
            caseId,
            executionId,
            query,
            category,
            sourceType,
            candidateSize: Math.Max(topK * 2, topK),
            cancellationToken);

        return await RerankEvidenceAsync(query, candidates, topK, cancellationToken);
    }

    private async Task<IReadOnlyList<EvidenceSearchCandidate>> SearchCandidatesAsync(
        string caseId,
        string executionId,
        string query,
        string? category,
        string? sourceType,
        int candidateSize,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = NormalizeSearchQuery(query);
        return await ExecuteSearchAsync(
            normalizedQuery,
            BuildSearchFilter(caseId, executionId, category, sourceType),
            candidateSize,
            cancellationToken);
    }

    private static string NormalizeSearchQuery(string query)
    {
        var trimmed = query.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("Search query is required.", nameof(query));
        }

        return trimmed.Length <= MaxSearchQueryLength
            ? trimmed
            : trimmed[..MaxSearchQueryLength];
    }

    private async Task<IReadOnlyList<EvidenceSearchCandidate>> ExecuteSearchAsync(
        string normalizedQuery,
        string filter,
        int candidateSize,
        CancellationToken cancellationToken)
    {
        var searchOptions = new SearchOptions
        {
            Size = candidateSize,
            Filter = filter,
            Select = { "documentId", "documentType", "category", "chunkText" }
        };

        try
        {
            var queryEmbedding = (await _embeddingService.EmbedAsync([normalizedQuery], cancellationToken)).Single();

            searchOptions.VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(queryEmbedding)
                    {
                        KNearestNeighborsCount = candidateSize,
                        Fields = { "embedding" }
                    }
                }
            };

            return await CollectSearchResultsAsync(null, searchOptions, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return await CollectSearchResultsAsync(normalizedQuery, searchOptions, cancellationToken);
        }
    }

    private static string BuildSearchFilter(
        string caseId,
        string executionId,
        string? category,
        string? sourceType)
    {
        var filter = $"caseId eq '{EscapeFilterValue(caseId)}' and executionId eq '{EscapeFilterValue(executionId)}' and documentType ne '{MetadataDocumentType}'";
        if (!string.IsNullOrWhiteSpace(category))
        {
            filter += $" and category eq '{EscapeFilterValue(category)}'";
        }

        if (!string.IsNullOrWhiteSpace(sourceType))
        {
            filter += $" and sourceType eq '{EscapeFilterValue(sourceType)}'";
        }

        return filter;
    }

    private async Task<IReadOnlyList<EvidenceSearchCandidate>> CollectSearchResultsAsync(
        string? lexicalQuery,
        SearchOptions searchOptions,
        CancellationToken cancellationToken)
    {
        var response = await _searchClient.SearchAsync<SearchDocument>(
            lexicalQuery,
            searchOptions,
            cancellationToken);
        var candidates = new List<EvidenceSearchCandidate>();

        await foreach (var result in response.Value.GetResultsAsync())
        {
            if (result.Document is not null)
            {
                candidates.Add(new EvidenceSearchCandidate
                {
                    DocumentId = GetSearchDocumentString(result.Document, "documentId"),
                    DocumentType = GetSearchDocumentString(result.Document, "documentType"),
                    Category = GetSearchDocumentString(result.Document, "category"),
                    ChunkText = GetSearchDocumentString(result.Document, "chunkText")
                });
            }
        }

        return candidates;
    }

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

    private async Task<IReadOnlyList<SignalMatch>> RerankEvidenceAsync(
        string query,
        IReadOnlyList<EvidenceSearchCandidate> candidates,
        int topK,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        if (candidates.Count <= topK)
        {
            return candidates
                .Select(candidate => new SignalMatch
                {
                    DocumentId = candidate.DocumentId ?? string.Empty,
                    DocumentType = candidate.DocumentType ?? string.Empty,
                    Category = candidate.Category ?? string.Empty,
                    Snippet = TruncateSnippet(candidate.ChunkText),
                    Score = 1
                })
                .ToArray();
        }

        try
        {
            var reranked = await _rerankService.RerankAsync(
                NormalizeSearchQuery(query),
                candidates.Select(candidate => candidate.ChunkText ?? string.Empty).ToArray(),
                topK,
                cancellationToken);

            return reranked
                .Select(result => new SignalMatch
                {
                    DocumentId = candidates[result.Index].DocumentId ?? string.Empty,
                    DocumentType = candidates[result.Index].DocumentType ?? string.Empty,
                    Category = candidates[result.Index].Category ?? string.Empty,
                    Snippet = TruncateSnippet(candidates[result.Index].ChunkText),
                    Score = result.Score
                })
                .ToArray();
        }
        catch (HttpRequestException)
        {
            return candidates
                .Take(topK)
                .Select(candidate => new SignalMatch
                {
                    DocumentId = candidate.DocumentId ?? string.Empty,
                    DocumentType = candidate.DocumentType ?? string.Empty,
                    Category = candidate.Category ?? string.Empty,
                    Snippet = TruncateSnippet(candidate.ChunkText),
                    Score = 1
                })
                .ToArray();
        }
    }

    private static string EscapeFilterValue(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string TruncateSnippet(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= MaxEvidenceSnippetLength)
        {
            return text ?? string.Empty;
        }

        return text[..MaxEvidenceSnippetLength] + "...";
    }

    private sealed class EvidenceSearchCandidate
    {
        public string? DocumentId { get; set; }

        public string? DocumentType { get; set; }

        public string? Category { get; set; }

        public string? ChunkText { get; set; }
    }
}
