using System.Text.Json;
using Azure;
using Azure.Identity;
using Azure.Search.Documents.KnowledgeBases;
using Azure.Search.Documents.KnowledgeBases.Models;
using GrokInventoryAndTrend.Mcp.Options;
using Microsoft.Extensions.Options;

namespace GrokInventoryAndTrend.Mcp.Adapters;

public sealed class FoundryIqRetrievalService
{
    private readonly FoundryIqOptions _options;
    private readonly ILogger<FoundryIqRetrievalService> _logger;
    private readonly DefaultAzureCredential _credential = new();
    private readonly Dictionary<string, KnowledgeBaseRetrievalClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public FoundryIqRetrievalService(
        IOptions<FoundryIqOptions> options,
        ILogger<FoundryIqRetrievalService> logger)
    {
        _options = options.Value;
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

        var client = GetClient(knowledgeBaseName);
        var request = BuildRequest(query, knowledgeSourceName, topK, filterAddOn);

        _logger.LogDebug(
            "Querying Foundry IQ knowledge base {KnowledgeBaseName} with query length {QueryLength}",
            knowledgeBaseName,
            query.Length);

        Response<KnowledgeBaseRetrievalResponse> response =
            await client.RetrieveAsync(request, cancellationToken);

        var documents = ParseDocuments(response.Value, knowledgeSourceName);

        if (postFilter is not null)
        {
            documents = documents.Where(postFilter).ToArray();
        }

        return documents
            .Take(Math.Max(1, topK))
            .ToArray();
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

        var sourceParams = new AzureBlobKnowledgeSourceParams(knowledgeSourceName)
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
                    SourcePath = ExtractBlobPath(reference)
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

        return string.Empty;
    }

    private static string ExtractReferenceTitle(KnowledgeBaseReference reference)
    {
        if (reference.SourceData is not null
            && reference.SourceData.TryGetValue("title", out var title))
        {
            return ReadBinaryDataAsString(title) ?? string.Empty;
        }

        return GetDocKey(reference) ?? string.Empty;
    }

    private static string? ExtractBlobPath(KnowledgeBaseReference reference)
    {
        if (reference is KnowledgeBaseAzureBlobReference blobReference)
        {
            return blobReference.BlobUrl?.ToString();
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

    private static bool ContainsPolicyRef(FoundryIqDocument document, string policyRef) =>
        document.Content.Contains($"Policy Ref: {policyRef}", StringComparison.OrdinalIgnoreCase)
        || string.Equals(document.Title, policyRef, StringComparison.OrdinalIgnoreCase);

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

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.SearchEndpoint))
        {
            throw new InvalidOperationException("FoundryIq:SearchEndpoint is required.");
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
