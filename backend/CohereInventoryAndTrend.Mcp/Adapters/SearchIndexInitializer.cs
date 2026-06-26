using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using CohereInventoryAndTrend.Mcp.Models;
using CohereInventoryAndTrend.Mcp.Options;
using Microsoft.Extensions.Options;

namespace CohereInventoryAndTrend.Mcp.Adapters;

public sealed class SearchIndexInitializer
{
    private readonly SearchIndexClient _indexClient;
    private readonly AzureSearchOptions _options;

    public SearchIndexInitializer(SearchIndexClient indexClient, IOptions<AzureSearchOptions> options)
    {
        _indexClient = indexClient;
        _options = options.Value;
    }

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureEvidenceIndexAsync(cancellationToken);
        await EnsurePolicyIndexAsync(cancellationToken);
    }

    private async Task EnsureEvidenceIndexAsync(CancellationToken cancellationToken)
    {
        var indexName = _options.EvidenceIndexName;
        if (await IndexExistsAsync(indexName, cancellationToken))
        {
            return;
        }

        var fields = new List<SearchField>
        {
            new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
            new SearchableField("caseId") { IsFilterable = true, IsFacetable = true },
            new SearchableField("executionId") { IsFilterable = true, IsFacetable = true },
            new SearchableField("documentId") { IsFilterable = true },
            new SearchableField("documentType") { IsFilterable = true, IsFacetable = true },
            new SearchableField("category") { IsFilterable = true, IsFacetable = true },
            new SearchableField("sourceType") { IsFilterable = true, IsFacetable = true },
            new SearchableField("sourceKey") { IsFilterable = true },
            new SearchableField("contentHash") { IsFilterable = true },
            new SimpleField("indexedAtUtc", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
            new SimpleField("sourceDocumentCount", SearchFieldDataType.Int32) { IsFilterable = true },
            new SimpleField("chunkCount", SearchFieldDataType.Int32) { IsFilterable = true },
            new SearchableField("chunkText") { IsFilterable = false },
            new SearchableField("sourcePath") { IsFilterable = false },
            new SearchField("embedding", SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                IsSearchable = true,
                VectorSearchDimensions = _options.VectorDimensions,
                VectorSearchProfileName = "default-vector-profile"
            }
        };

        var index = new SearchIndex(indexName)
        {
            Fields = fields,
            VectorSearch = new VectorSearch
            {
                Profiles =
                {
                    new VectorSearchProfile("default-vector-profile", "default-hnsw-config")
                },
                Algorithms =
                {
                    new HnswAlgorithmConfiguration("default-hnsw-config")
                }
            }
        };

        await _indexClient.CreateIndexAsync(index, cancellationToken);
    }

    private async Task EnsurePolicyIndexAsync(CancellationToken cancellationToken)
    {
        var indexName = _options.PolicyIndexName;
        if (await IndexExistsAsync(indexName, cancellationToken))
        {
            return;
        }

        var fields = new List<SearchField>
        {
            new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
            new SearchableField("policyRef") { IsFilterable = true, IsFacetable = true },
            new SearchableField("documentType") { IsFilterable = true, IsFacetable = true },
            new SearchableField("rule"),
            new SearchableField("threshold"),
            new SearchableField("action"),
            new SearchableField("exception"),
            new SearchableField("fullText"),
            new SearchableField("contentHash") { IsFilterable = true },
            new SearchField("embedding", SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                IsSearchable = true,
                VectorSearchDimensions = _options.VectorDimensions,
                VectorSearchProfileName = "default-vector-profile"
            }
        };

        var index = new SearchIndex(indexName)
        {
            Fields = fields,
            VectorSearch = new VectorSearch
            {
                Profiles =
                {
                    new VectorSearchProfile("default-vector-profile", "default-hnsw-config")
                },
                Algorithms =
                {
                    new HnswAlgorithmConfiguration("default-hnsw-config")
                }
            }
        };

        await _indexClient.CreateIndexAsync(index, cancellationToken);
    }

    private async Task<bool> IndexExistsAsync(string indexName, CancellationToken cancellationToken)
    {
        try
        {
            await _indexClient.GetIndexAsync(indexName, cancellationToken);
            return true;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return false;
        }
    }
}

public static class SearchClientFactory
{
    public static SearchIndexClient CreateIndexClient(AzureSearchOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Endpoint);

        var endpoint = new Uri(options.Endpoint);
        return new SearchIndexClient(endpoint, new DefaultAzureCredential());
    }

    public static SearchClient CreateSearchClient(AzureSearchOptions options, string indexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Endpoint);

        var endpoint = new Uri(options.Endpoint);
        return new SearchClient(endpoint, indexName, new DefaultAzureCredential());
    }
}
