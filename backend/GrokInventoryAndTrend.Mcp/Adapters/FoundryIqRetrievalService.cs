using System.Text.Json;
using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.KnowledgeBases;
using Azure.Search.Documents.KnowledgeBases.Models;
using Azure.Search.Documents.Models;
using GrokInventoryAndTrend.Mcp.Options;
using Microsoft.Extensions.Options;

namespace GrokInventoryAndTrend.Mcp.Adapters;

public sealed class FoundryIqRetrievalService
{
    private readonly FoundryIqOptions _options;
    private readonly PolicyParser _policyParser;
    private readonly ILogger<FoundryIqRetrievalService> _logger;
    private readonly DefaultAzureCredential _credential = new();
    private readonly Dictionary<string, KnowledgeBaseRetrievalClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private SearchClient? _searchClient;

    public FoundryIqRetrievalService(
        IOptions<FoundryIqOptions> options,
        PolicyParser policyParser,
        ILogger<FoundryIqRetrievalService> logger)
    {
        _options = options.Value;
        _policyParser = policyParser;
        _logger = logger;
    }

    public Task<IReadOnlyList<FoundryIqDocument>> RetrievePoliciesAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default) =>
        RetrieveAsync(
            _options.PolicyKnowledgeBaseName,
            _options.PolicyKnowledgeSourceName,
            query,
            topK,
            filterAddOn: null,
            postFilter: null,
            cancellationToken);

    public async Task<IReadOnlyList<FoundryIqDocument>> RetrievePolicyByRefAsync(
        string policyRef,
        CancellationToken cancellationToken = default)
    {
        var documents = await RetrieveAsync(
            _options.PolicyKnowledgeBaseName,
            _options.PolicyKnowledgeSourceName,
            $"Policy Ref: {policyRef}",
            topK: 3,
            filterAddOn: null,
            postFilter: document => ContainsPolicyRef(document, policyRef),
            cancellationToken);

        return documents
            .Where(document => ContainsPolicyRef(document, policyRef))
            .Take(1)
            .ToArray();
    }

    private async Task<IReadOnlyList<FoundryIqDocument>> RetrieveAsync(
        string knowledgeBaseName,
        string knowledgeSourceName,
        string query,
        int topK,
        string? filterAddOn,
        Func<FoundryIqDocument, bool>? postFilter,
        CancellationToken cancellationToken)
    {
        ValidateConfiguration();

        try
        {
            var client = GetClient(knowledgeBaseName);
            var request = BuildRequest(query, knowledgeSourceName, topK, filterAddOn);

            _logger.LogDebug(
                "Querying Foundry IQ knowledge base {KnowledgeBaseName} with query length {QueryLength}",
                knowledgeBaseName,
                query.Length);

            Response<KnowledgeBaseRetrievalResponse> response =
                await client.RetrieveAsync(request, cancellationToken).ConfigureAwait(false);

            IReadOnlyList<FoundryIqDocument> knowledgeBaseDocuments = ParseDocuments(response.Value, knowledgeSourceName);
            IReadOnlyList<FoundryIqDocument> filteredKnowledgeBaseDocuments =
                ApplyPostFilter(knowledgeBaseDocuments, postFilter, topK);

            if (ContainsParseablePolicyDocuments(filteredKnowledgeBaseDocuments))
            {
                return filteredKnowledgeBaseDocuments;
            }

            _logger.LogWarning(
                "Knowledge base {KnowledgeBaseName} returned no parseable policy documents for query '{Query}'. Falling back to direct index query against {IndexName}.",
                knowledgeBaseName,
                query,
                _options.PolicyIndexName);
        }
        catch (RequestFailedException exception)
        {
            _logger.LogWarning(
                exception,
                "Knowledge base retrieval failed for {KnowledgeBaseName}. Falling back to direct index query against {IndexName}.",
                knowledgeBaseName,
                _options.PolicyIndexName);
        }

        IReadOnlyList<FoundryIqDocument> indexDocuments = await RetrieveFromSearchIndexAsync(
                query,
                topK,
                postFilter,
                cancellationToken)
            .ConfigureAwait(false);

        return ApplyPostFilter(indexDocuments, postFilter, topK);
    }

    private KnowledgeBaseRetrievalRequest BuildRequest(
        string query,
        string knowledgeSourceName,
        int topK,
        string? filterAddOn)
    {
        var request = new KnowledgeBaseRetrievalRequest
        {
            MaxOutputSizeInTokens = _options.MaxOutputSizeInTokens
        };

        request.Intents.Add(new KnowledgeRetrievalSemanticIntent(query));

        var sourceParams = new SearchIndexKnowledgeSourceParams(knowledgeSourceName)
        {
            IncludeReferences = true,
            IncludeReferenceSourceData = true
        };

        request.KnowledgeSourceParams.Add(sourceParams);
        return request;
    }

    private static IReadOnlyList<FoundryIqDocument> ParseDocuments(
        KnowledgeBaseRetrievalResponse response,
        string knowledgeSourceName)
    {
        var documents = new List<FoundryIqDocument>();

        if (response.References is not null)
        {
            foreach (var reference in response.References)
            {
                var content = ExtractReferenceContent(reference);
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                documents.Add(new FoundryIqDocument
                {
                    Id = reference.Id ?? GetDocKey(reference) ?? Guid.NewGuid().ToString("N"),
                    Content = content,
                    Title = ExtractReferenceTitle(reference),
                    Score = reference.RerankerScore ?? 1,
                    SourcePath = ExtractSourcePath(reference)
                });
            }
        }

        if (documents.Count > 0)
        {
            return documents;
        }

        foreach (var message in response.Response)
        {
            foreach (var content in message.Content)
            {
                if (content is not KnowledgeBaseMessageTextContent textContent
                    || string.IsNullOrWhiteSpace(textContent.Text))
                {
                    continue;
                }

                documents.AddRange(ParseGroundingPayload(textContent.Text, knowledgeSourceName));
            }
        }

        return documents;
    }

    private static IEnumerable<FoundryIqDocument> ParseGroundingPayload(
        string payload,
        string knowledgeSourceName)
    {
        if (!TryParseJsonArray(payload, out var elements))
        {
            yield return new FoundryIqDocument
            {
                Id = Guid.NewGuid().ToString("N"),
                Content = payload,
                Title = knowledgeSourceName,
                Score = 1
            };
            yield break;
        }

        foreach (var element in elements)
        {
            var content = element.TryGetProperty("content", out var contentElement)
                ? contentElement.GetString()
                : element.GetRawText();

            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            yield return new FoundryIqDocument
            {
                Id = element.TryGetProperty("ref_id", out var refId)
                    ? refId.GetString() ?? Guid.NewGuid().ToString("N")
                    : Guid.NewGuid().ToString("N"),
                Content = content,
                Title = element.TryGetProperty("title", out var title)
                    ? title.GetString() ?? string.Empty
                    : string.Empty,
                Score = 1
            };
        }
    }

    private static bool TryParseJsonArray(string payload, out JsonElement[] elements)
    {
        elements = [];

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            elements = document.RootElement.EnumerateArray().ToArray();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ExtractReferenceContent(KnowledgeBaseReference reference)
    {
        if (reference.SourceData is null)
        {
            return string.Empty;
        }

        foreach (var key in new[] { "content", "chunkText", "fullText", "text", "body" })
        {
            if (reference.SourceData.TryGetValue(key, out var value))
            {
                var text = ReadBinaryDataAsString(value);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return BuildPolicyContent(
            ReadSourceDataAsString(reference.SourceData, "policyRef") ?? ReadSourceDataAsString(reference.SourceData, "title"),
            ReadSourceDataAsString(reference.SourceData, "rule"),
            ReadSourceDataAsString(reference.SourceData, "threshold"),
            ReadSourceDataAsString(reference.SourceData, "action"),
            ReadSourceDataAsString(reference.SourceData, "exception"));
    }

    private static string ExtractReferenceTitle(KnowledgeBaseReference reference)
    {
        if (reference.SourceData is not null
            && reference.SourceData.TryGetValue("title", out var title))
        {
            return ReadBinaryDataAsString(title) ?? string.Empty;
        }

        if (reference.SourceData is not null
            && reference.SourceData.TryGetValue("policyRef", out var policyRef))
        {
            return ReadBinaryDataAsString(policyRef) ?? string.Empty;
        }

        return GetDocKey(reference) ?? string.Empty;
    }

    private static string? ExtractSourcePath(KnowledgeBaseReference reference)
    {
        if (reference is KnowledgeBaseSearchIndexReference searchIndexReference)
        {
            return searchIndexReference.DocKey;
        }

        return GetDocKey(reference);
    }

    private static string? GetDocKey(KnowledgeBaseReference reference) =>
        reference switch
        {
            KnowledgeBaseSearchIndexReference searchIndexReference => searchIndexReference.DocKey,
            _ => reference.Id
        };

    private static string? ReadBinaryDataAsString(BinaryData? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return value.ToString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? ReadSourceDataAsString(
        IDictionary<string, BinaryData> sourceData,
        string key) =>
        sourceData.TryGetValue(key, out var value)
            ? ReadBinaryDataAsString(value)
            : null;

    private static string BuildPolicyContent(
        string? policyRef,
        string? rule,
        string? threshold,
        string? action,
        string? exception)
    {
        var parts = new List<string>(5);

        if (!string.IsNullOrWhiteSpace(policyRef))
        {
            parts.Add($"Policy Ref: {policyRef.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(rule))
        {
            parts.Add($"Rule: {rule.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(threshold))
        {
            parts.Add($"Threshold: {threshold.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            parts.Add($"Action: {action.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(exception))
        {
            parts.Add($"Exception: {exception.Trim()}");
        }

        return string.Join('\n', parts);
    }

    private static bool ContainsPolicyRef(FoundryIqDocument document, string policyRef) =>
        document.Content.Contains($"Policy Ref: {policyRef}", StringComparison.OrdinalIgnoreCase)
        || string.Equals(document.Title, policyRef, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<FoundryIqDocument> ApplyPostFilter(
        IReadOnlyList<FoundryIqDocument> documents,
        Func<FoundryIqDocument, bool>? postFilter,
        int topK)
    {
        IEnumerable<FoundryIqDocument> filtered = postFilter is null
            ? documents
            : documents.Where(postFilter);

        return filtered
            .Take(Math.Max(1, topK))
            .ToArray();
    }

    private bool ContainsParseablePolicyDocuments(IReadOnlyList<FoundryIqDocument> documents) =>
        documents.Any(document => TryParsePolicyDocument(document.Content));

    private bool TryParsePolicyDocument(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        try
        {
            return _policyParser.Parse(content).Count > 0;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private async Task<IReadOnlyList<FoundryIqDocument>> RetrieveFromSearchIndexAsync(
        string query,
        int topK,
        Func<FoundryIqDocument, bool>? postFilter,
        CancellationToken cancellationToken)
    {
        var searchClient = GetSearchClient();
        var searchOptions = new SearchOptions
        {
            Size = Math.Max(1, topK),
            IncludeTotalCount = true
        };

        foreach (string field in new[] { "policyRef", "title", "content", "rule", "threshold", "action", "exception" })
        {
            searchOptions.SearchFields.Add(field);
            searchOptions.Select.Add(field);
        }

        string searchText = query;
        if (TryExtractPolicyRefQuery(query, out string? policyRef))
        {
            searchText = "*";
            searchOptions.Filter = $"policyRef eq '{EscapeFilterValue(policyRef!)}'";
        }

        _logger.LogDebug(
            "Querying Search index {IndexName} with search text '{SearchText}' and filter '{Filter}'.",
            _options.PolicyIndexName,
            searchText,
            searchOptions.Filter ?? string.Empty);

        Response<SearchResults<SearchDocument>> response =
            await searchClient.SearchAsync<SearchDocument>(searchText, searchOptions, cancellationToken).ConfigureAwait(false);

        var documents = new List<FoundryIqDocument>();
        await foreach (SearchResult<SearchDocument> result in response.Value.GetResultsAsync().ConfigureAwait(false))
        {
            FoundryIqDocument? document = CreateDocumentFromSearchResult(result);
            if (document is null)
            {
                continue;
            }

            if (postFilter is not null && !postFilter(document))
            {
                continue;
            }

            documents.Add(document);
        }

        return documents;
    }

    private static FoundryIqDocument? CreateDocumentFromSearchResult(SearchResult<SearchDocument> result)
    {
        SearchDocument source = result.Document;
        string title = ReadSearchDocumentString(source, "title")
            ?? ReadSearchDocumentString(source, "policyRef")
            ?? string.Empty;
        string content = ReadSearchDocumentString(source, "content")
            ?? BuildPolicyContent(
                ReadSearchDocumentString(source, "policyRef") ?? title,
                ReadSearchDocumentString(source, "rule"),
                ReadSearchDocumentString(source, "threshold"),
                ReadSearchDocumentString(source, "action"),
                ReadSearchDocumentString(source, "exception"));

        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        return new FoundryIqDocument
        {
            Id = ReadSearchDocumentString(source, "id") ?? title ?? Guid.NewGuid().ToString("N"),
            Content = content,
            Title = title ?? string.Empty,
            Score = result.Score ?? 1,
            SourcePath = ReadSearchDocumentString(source, "policyRef")
        };
    }

    private static string? ReadSearchDocumentString(SearchDocument document, string fieldName)
    {
        if (!document.TryGetValue(fieldName, out object? rawValue) || rawValue is null)
        {
            return null;
        }

        return rawValue switch
        {
            string text => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            JsonElement element => element.ToString(),
            _ => rawValue.ToString()
        };
    }

    private static bool TryExtractPolicyRefQuery(string query, out string? policyRef)
    {
        const string prefix = "Policy Ref:";
        if (query.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            string candidate = query[prefix.Length..].Trim();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                policyRef = candidate;
                return true;
            }
        }

        policyRef = null;
        return false;
    }

    private static string EscapeFilterValue(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private KnowledgeBaseRetrievalClient GetClient(string knowledgeBaseName)
    {
        if (_clients.TryGetValue(knowledgeBaseName, out var cachedClient))
        {
            return cachedClient;
        }

        var client = new KnowledgeBaseRetrievalClient(
            new Uri(_options.SearchEndpoint),
            knowledgeBaseName,
            _credential);

        _clients[knowledgeBaseName] = client;
        return client;
    }

    private SearchClient GetSearchClient()
    {
        if (_searchClient is not null)
        {
            return _searchClient;
        }

        var endpoint = new Uri(_options.SearchEndpoint);
        _searchClient = string.IsNullOrWhiteSpace(_options.SearchAdminKey)
            ? new SearchClient(endpoint, _options.PolicyIndexName, _credential)
            : new SearchClient(endpoint, _options.PolicyIndexName, new AzureKeyCredential(_options.SearchAdminKey));

        return _searchClient;
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.SearchEndpoint))
        {
            throw new InvalidOperationException("FoundryIq:SearchEndpoint is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.PolicyIndexName))
        {
            throw new InvalidOperationException("FoundryIq:PolicyIndexName is required.");
        }
    }
}

public sealed class FoundryIqDocument
{
    public required string Id { get; init; }

    public required string Content { get; init; }

    public string Title { get; init; } = string.Empty;

    public double Score { get; init; } = 1;

    public string? SourcePath { get; init; }
}
