using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.KnowledgeBases.Models;
using Azure.Search.Documents.Models;
using GrokInventoryAndTrend.Mcp.Adapters;
using GrokInventoryAndTrend.Mcp.Models;
using GrokInventoryAndTrend.Mcp.Options;
using Microsoft.Extensions.Options;

namespace GrokInventoryAndTrend.Mcp.Startup;

public sealed class FoundryIqBootstrapRunner
{
    private const string ContentFieldName = "content";
    private const string ContentVectorFieldName = "contentVector";
    private const string VectorProfileName = "policy-vector-profile";
    private const string VectorAlgorithmName = "policy-hnsw";
    private const string VectorizerName = "policy-vectorizer";

    private readonly FoundryIqBootstrapOptions _options;
    private readonly PolicyParser _policyParser;
    private readonly ILogger<FoundryIqBootstrapRunner> _logger;
    private readonly DefaultAzureCredential _credential = new();
    private int _expectedPolicyCount;

    public FoundryIqBootstrapRunner(
        IOptions<FoundryIqBootstrapOptions> options,
        PolicyParser policyParser,
        ILogger<FoundryIqBootstrapRunner> logger)
    {
        _options = options.Value;
        _policyParser = policyParser;
        _logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var currentStep = "initialization";

        try
        {
            currentStep = "validate configuration";
            _logger.LogInformation("Foundry IQ bootstrap starting step: {Step}", currentStep);
            ValidateOptions();
            LogBootstrapContext();

            currentStep = "load policies";
            _logger.LogInformation("Foundry IQ bootstrap starting step: {Step}", currentStep);
            var policies = _policyParser.LoadFromJsonFile(_options.PolicyFilePath);
            _expectedPolicyCount = policies.Count;
            _logger.LogInformation(
                "Loaded {PolicyCount} policies from {PolicyFilePath}.",
                policies.Count,
                _options.PolicyFilePath);

            var indexClient = CreateSearchIndexClient();

            currentStep = "ensure search index";
            _logger.LogInformation("Foundry IQ bootstrap starting step: {Step}", currentStep);
            await EnsureSearchIndexAsync(indexClient, cancellationToken);

            currentStep = "upload policies to search index";
            _logger.LogInformation("Foundry IQ bootstrap starting step: {Step}", currentStep);
            await UploadPoliciesToIndexAsync(policies, cancellationToken);

            currentStep = "remove legacy knowledge artifacts";
            _logger.LogInformation("Foundry IQ bootstrap starting step: {Step}", currentStep);
            await RemoveLegacyKnowledgeArtifactsAsync(indexClient, cancellationToken);

            currentStep = "ensure knowledge source";
            _logger.LogInformation("Foundry IQ bootstrap starting step: {Step}", currentStep);
            await EnsureKnowledgeSourceAsync(indexClient, _options.PolicyKnowledgeSourceName, cancellationToken);

            currentStep = "wait for knowledge source readiness";
            _logger.LogInformation("Foundry IQ bootstrap starting step: {Step}", currentStep);
            await WaitForKnowledgeSourceReadyAsync(indexClient, _options.PolicyKnowledgeSourceName, cancellationToken);

            currentStep = "ensure knowledge base";
            _logger.LogInformation("Foundry IQ bootstrap starting step: {Step}", currentStep);
            await EnsureKnowledgeBaseAsync(
                indexClient,
                _options.PolicyKnowledgeBaseName,
                _options.PolicyKnowledgeSourceName,
                cancellationToken);

            _logger.LogInformation("Foundry IQ policy bootstrap completed successfully.");
            return 0;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Foundry IQ policy bootstrap failed during step '{Step}'. {Details}",
                currentStep,
                DescribeException(exception));
            return 1;
        }
    }

    private void LogBootstrapContext()
    {
        _logger.LogInformation(
            "Bootstrap configuration: SearchEndpoint={SearchEndpoint}, PolicyIndexName={PolicyIndexName}, KnowledgeSourceName={KnowledgeSourceName}, KnowledgeBaseName={KnowledgeBaseName}, FoundryResourceUri={FoundryResourceUri}, EmbedDeploymentName={EmbedDeploymentName}, EmbedModelName={EmbedModelName}, EmbeddingDimensions={EmbeddingDimensions}",
            _options.SearchEndpoint,
            _options.PolicyIndexName,
            _options.PolicyKnowledgeSourceName,
            _options.PolicyKnowledgeBaseName,
            _options.FoundryResourceUri,
            _options.EmbedDeploymentName,
            _options.EmbedModelName,
            _options.EmbeddingDimensions);
    }

    private static string DescribeException(Exception exception) =>
        exception is RequestFailedException requestFailed
            ? $"Status={requestFailed.Status}, ErrorCode={requestFailed.ErrorCode}, Message={requestFailed.Message}"
            : exception.Message;

    private async Task EnsureSearchIndexAsync(SearchIndexClient indexClient, CancellationToken cancellationToken)
    {
        var vectorizer = new AzureOpenAIVectorizer(VectorizerName)
        {
            Parameters = new AzureOpenAIVectorizerParameters
            {
                ResourceUri = new Uri(_options.FoundryResourceUri),
                DeploymentName = _options.EmbedDeploymentName,
                ModelName = _options.EmbedModelName
            }
        };

        var index = new SearchIndex(_options.PolicyIndexName)
        {
            Fields =
            {
                new SimpleField("id", SearchFieldDataType.String)
                {
                    IsKey = true,
                    IsFilterable = true,
                    IsSortable = true,
                    IsFacetable = true
                },
                new SearchField("policyRef", SearchFieldDataType.String)
                {
                    IsFilterable = true,
                    IsSortable = true,
                    IsFacetable = true
                },
                new SearchField(ContentFieldName, SearchFieldDataType.String)
                {
                    IsSearchable = true,
                    IsFilterable = false,
                    IsSortable = false,
                    IsFacetable = false
                },
                new SearchField("rule", SearchFieldDataType.String),
                new SearchField("threshold", SearchFieldDataType.String),
                new SearchField("action", SearchFieldDataType.String),
                new SearchField("exception", SearchFieldDataType.String),
                new SearchField("title", SearchFieldDataType.String),
                new SearchField(ContentVectorFieldName, SearchFieldDataType.Collection(SearchFieldDataType.Single))
                {
                    IsSearchable = true,
                    VectorSearchDimensions = _options.EmbeddingDimensions,
                    VectorSearchProfileName = VectorProfileName
                }
            },
            VectorSearch = new VectorSearch
            {
                Profiles =
                {
                    new VectorSearchProfile(VectorProfileName, VectorAlgorithmName)
                    {
                        VectorizerName = VectorizerName
                    }
                },
                Algorithms =
                {
                    new HnswAlgorithmConfiguration(VectorAlgorithmName)
                },
                Vectorizers = { vectorizer }
            },
            SemanticSearch = new SemanticSearch
            {
                DefaultConfigurationName = _options.SemanticConfigurationName,
                Configurations =
                {
                    new SemanticConfiguration(
                        _options.SemanticConfigurationName,
                        new SemanticPrioritizedFields
                        {
                            TitleField = new SemanticField("title"),
                            ContentFields = { new SemanticField(ContentFieldName) }
                        })
                }
            }
        };

        await indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: cancellationToken);
        _logger.LogInformation("Ensured search index {IndexName}.", _options.PolicyIndexName);
    }

    private async Task UploadPoliciesToIndexAsync(
        IReadOnlyList<PolicyEntry> policies,
        CancellationToken cancellationToken)
    {
        using var embeddingClient = new PolicyEmbeddingClient(
            _options.FoundryResourceUri,
            _options.EmbedDeploymentName,
            _credential,
            _logger);

        var searchClient = CreateSearchClient();

        var documents = new List<SearchDocument>(policies.Count);

        foreach (var policy in policies)
        {
            try
            {
                var embedding = await embeddingClient.EmbedAsync(policy.FullText, cancellationToken);
                documents.Add(new SearchDocument
                {
                    ["id"] = policy.PolicyRef,
                    ["policyRef"] = policy.PolicyRef,
                    [ContentFieldName] = policy.FullText,
                    ["rule"] = policy.Rule,
                    ["threshold"] = policy.Threshold,
                    ["action"] = policy.Action,
                    ["exception"] = policy.Exception,
                    ["title"] = policy.PolicyRef,
                    [ContentVectorFieldName] = embedding
                });
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to embed policy {PolicyRef}. {Details}",
                    policy.PolicyRef,
                    DescribeException(exception));
                throw;
            }
        }

        var batch = IndexDocumentsBatch.Upload(documents);
        var result = await searchClient.IndexDocumentsAsync(batch, cancellationToken: cancellationToken);

        if (result.Value.Results.Any(documentResult => !documentResult.Succeeded))
        {
            var failures = result.Value.Results
                .Where(documentResult => !documentResult.Succeeded)
                .Select(documentResult => $"{documentResult.Key}: {documentResult.ErrorMessage}")
                .ToArray();

            throw new InvalidOperationException(
                $"Failed to upload one or more policy documents: {string.Join("; ", failures)}");
        }

        _logger.LogInformation(
            "Uploaded {PolicyCount} policy documents to search index {IndexName}.",
            policies.Count,
            _options.PolicyIndexName);
    }

    private async Task RemoveLegacyKnowledgeArtifactsAsync(
        SearchIndexClient indexClient,
        CancellationToken cancellationToken)
    {
        try
        {
            await indexClient.DeleteKnowledgeBaseAsync(_options.PolicyKnowledgeBaseName, cancellationToken: cancellationToken);
            _logger.LogInformation("Deleted existing knowledge base {KnowledgeBaseName}.", _options.PolicyKnowledgeBaseName);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            _logger.LogDebug("Knowledge base {KnowledgeBaseName} did not exist.", _options.PolicyKnowledgeBaseName);
        }

        try
        {
            await indexClient.DeleteKnowledgeSourceAsync(_options.PolicyKnowledgeSourceName, cancellationToken: cancellationToken);
            _logger.LogInformation("Deleted existing knowledge source {KnowledgeSourceName}.", _options.PolicyKnowledgeSourceName);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            _logger.LogDebug("Knowledge source {KnowledgeSourceName} did not exist.", _options.PolicyKnowledgeSourceName);
        }
    }

    private async Task EnsureKnowledgeSourceAsync(
        SearchIndexClient indexClient,
        string knowledgeSourceName,
        CancellationToken cancellationToken)
    {
        var searchIndexParameters = new SearchIndexKnowledgeSourceParameters(_options.PolicyIndexName)
        {
            SemanticConfigurationName = _options.SemanticConfigurationName,
            SearchFields =
            {
                new SearchIndexFieldReference(ContentFieldName)
            },
            SourceDataFields =
            {
                new SearchIndexFieldReference("id"),
                new SearchIndexFieldReference("policyRef"),
                new SearchIndexFieldReference(ContentFieldName),
                new SearchIndexFieldReference("rule"),
                new SearchIndexFieldReference("threshold"),
                new SearchIndexFieldReference("action"),
                new SearchIndexFieldReference("exception"),
                new SearchIndexFieldReference("title")
            }
        };

        var knowledgeSource = new SearchIndexKnowledgeSource(knowledgeSourceName, searchIndexParameters);

        await indexClient.CreateOrUpdateKnowledgeSourceAsync(knowledgeSource, onlyIfUnchanged: false, cancellationToken);
        _logger.LogInformation("Ensured search-index knowledge source {KnowledgeSourceName}.", knowledgeSourceName);
    }

    private async Task WaitForKnowledgeSourceReadyAsync(
        SearchIndexClient indexClient,
        string knowledgeSourceName,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= _options.IndexerPollAttempts; attempt++)
        {
            var statusResponse = await indexClient.GetKnowledgeSourceStatusAsync(knowledgeSourceName, cancellationToken);
            var status = statusResponse.Value;
            var syncStatus = status.SynchronizationStatus.ToString();

            _logger.LogInformation(
                "Knowledge source {KnowledgeSourceName} synchronization status: {Status} (attempt {Attempt}/{MaxAttempts})",
                knowledgeSourceName,
                syncStatus,
                attempt,
                _options.IndexerPollAttempts);

            if (string.Equals(syncStatus, "Ready", StringComparison.OrdinalIgnoreCase)
                || string.Equals(syncStatus, "Idle", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(syncStatus, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Knowledge source '{knowledgeSourceName}' ingestion failed with status '{syncStatus}' after {attempt} attempts.");
            }

            if (await IsIndexReadyAsync(indexClient, cancellationToken))
            {
                _logger.LogInformation(
                    "Search index {IndexName} contains {DocumentCount} documents while knowledge source sync status is {Status}.",
                    _options.PolicyIndexName,
                    _expectedPolicyCount,
                    syncStatus);
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.IndexerPollDelaySeconds), cancellationToken);
        }

        throw new TimeoutException(
            $"Timed out waiting for knowledge source '{knowledgeSourceName}' to become ready.");
    }

    private async Task<bool> IsIndexReadyAsync(SearchIndexClient indexClient, CancellationToken cancellationToken)
    {
        try
        {
            var indexStats = await indexClient.GetIndexStatisticsAsync(_options.PolicyIndexName, cancellationToken);
            return indexStats.Value.DocumentCount >= _expectedPolicyCount;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return false;
        }
    }

    private async Task EnsureKnowledgeBaseAsync(
        SearchIndexClient indexClient,
        string knowledgeBaseName,
        string knowledgeSourceName,
        CancellationToken cancellationToken)
    {
        var knowledgeBase = new KnowledgeBase(
            knowledgeBaseName,
            [new KnowledgeSourceReference(knowledgeSourceName)])
        {
            Description = $"Inventory planning {knowledgeBaseName}"
        };

        await indexClient.CreateOrUpdateKnowledgeBaseAsync(knowledgeBase, onlyIfUnchanged: false, cancellationToken);
        _logger.LogInformation("Ensured knowledge base {KnowledgeBaseName}.", knowledgeBaseName);
    }

    private SearchIndexClient CreateSearchIndexClient()
    {
        var endpoint = new Uri(_options.SearchEndpoint);
        return string.IsNullOrWhiteSpace(_options.SearchAdminKey)
            ? new SearchIndexClient(endpoint, _credential)
            : new SearchIndexClient(endpoint, new Azure.AzureKeyCredential(_options.SearchAdminKey));
    }

    private SearchClient CreateSearchClient()
    {
        var endpoint = new Uri(_options.SearchEndpoint);
        return string.IsNullOrWhiteSpace(_options.SearchAdminKey)
            ? new SearchClient(endpoint, _options.PolicyIndexName, _credential)
            : new SearchClient(endpoint, _options.PolicyIndexName, new Azure.AzureKeyCredential(_options.SearchAdminKey));
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.SearchEndpoint))
        {
            throw new InvalidOperationException("FoundryIqBootstrap:SearchEndpoint is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.PolicyIndexName))
        {
            throw new InvalidOperationException("FoundryIqBootstrap:PolicyIndexName is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.FoundryResourceUri))
        {
            throw new InvalidOperationException("FoundryIqBootstrap:FoundryResourceUri is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.PolicyFilePath))
        {
            throw new InvalidOperationException("FoundryIqBootstrap:PolicyFilePath is required.");
        }
    }
}
