using Azure;
using Azure.Identity;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.KnowledgeBases.Models;
using Azure.Storage.Blobs;
using GrokInventoryAndTrend.Mcp.Adapters;
using GrokInventoryAndTrend.Mcp.Options;
using Microsoft.Extensions.Options;

namespace GrokInventoryAndTrend.Mcp.Startup;

public sealed class FoundryIqBootstrapRunner
{
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
        try
        {
            ValidateOptions();
            await UploadPoliciesAsync(cancellationToken);

            var indexClient = new SearchIndexClient(new Uri(_options.SearchEndpoint), _credential);

            await EnsureKnowledgeSourceAsync(
                indexClient,
                _options.PolicyKnowledgeSourceName,
                _options.PolicyContainerName,
                cancellationToken);

            await WaitForKnowledgeSourceReadyAsync(
                indexClient,
                _options.PolicyKnowledgeSourceName,
                _expectedPolicyCount,
                cancellationToken);

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
            _logger.LogError(exception, "Foundry IQ policy bootstrap failed.");
            return 1;
        }
    }

    private async Task UploadPoliciesAsync(CancellationToken cancellationToken)
    {
        var blobServiceClient = new BlobServiceClient(_options.StorageConnectionString);

        if (!File.Exists(_options.PolicyFilePath))
        {
            throw new FileNotFoundException($"Policy file was not found at '{_options.PolicyFilePath}'.");
        }

        var containerClient = blobServiceClient.GetBlobContainerClient(_options.PolicyContainerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var policyText = await File.ReadAllTextAsync(_options.PolicyFilePath, cancellationToken);
        var policies = _policyParser.Parse(policyText);

        foreach (var policy in policies)
        {
            var blobName = $"{policy.PolicyRef}.txt";
            var blobClient = containerClient.GetBlobClient(blobName);
            await blobClient.UploadAsync(
                BinaryData.FromString(policy.FullText),
                overwrite: true,
                cancellationToken);
        }

        _expectedPolicyCount = policies.Count;
        _logger.LogInformation("Uploaded {PolicyCount} policy documents to container {ContainerName}.", policies.Count, _options.PolicyContainerName);
    }

    private async Task EnsureKnowledgeSourceAsync(
        SearchIndexClient indexClient,
        string knowledgeSourceName,
        string containerName,
        CancellationToken cancellationToken)
    {
        var blobParameters = new AzureBlobKnowledgeSourceParameters(
            _options.StorageConnectionString,
            containerName)
        {
            IngestionParameters = BuildIngestionParameters()
        };

        var knowledgeSource = new AzureBlobKnowledgeSource(knowledgeSourceName, blobParameters);

        await indexClient.CreateOrUpdateKnowledgeSourceAsync(knowledgeSource, onlyIfUnchanged: false, cancellationToken);
        _logger.LogInformation("Ensured knowledge source {KnowledgeSourceName}.", knowledgeSourceName);
    }

    private KnowledgeSourceIngestionParameters BuildIngestionParameters() =>
        new()
        {
            EmbeddingModel = new KnowledgeSourceAzureOpenAIVectorizer
            {
                AzureOpenAIParameters = new AzureOpenAIVectorizerParameters
                {
                    ResourceUri = new Uri(_options.FoundryResourceUri),
                    DeploymentName = _options.EmbedDeploymentName,
                    ModelName = new AzureOpenAIModelName(_options.EmbedModelName)
                }
            },
            ContentExtractionMode = KnowledgeSourceContentExtractionMode.Minimal
        };

    private async Task WaitForKnowledgeSourceReadyAsync(
        SearchIndexClient indexClient,
        string knowledgeSourceName,
        int expectedDocumentCount,
        CancellationToken cancellationToken)
    {
        var indexerClient = new SearchIndexerClient(new Uri(_options.SearchEndpoint), _credential);
        var indexerName = $"{knowledgeSourceName}-indexer";

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
                    $"Knowledge source '{knowledgeSourceName}' ingestion failed with status '{syncStatus}'.");
            }

            if (await IsIndexerIngestionCompleteAsync(
                    indexClient,
                    indexerClient,
                    indexerName,
                    expectedDocumentCount,
                    cancellationToken))
            {
                _logger.LogInformation(
                    "Knowledge source {KnowledgeSourceName} indexer completed successfully while sync status remained {Status}.",
                    knowledgeSourceName,
                    syncStatus);
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.IndexerPollDelaySeconds), cancellationToken);
        }

        throw new TimeoutException(
            $"Timed out waiting for knowledge source '{knowledgeSourceName}' ingestion to complete.");
    }

    private static async Task<bool> IsIndexerIngestionCompleteAsync(
        SearchIndexClient indexClient,
        SearchIndexerClient indexerClient,
        string indexerName,
        int expectedDocumentCount,
        CancellationToken cancellationToken)
    {
        try
        {
            var indexerStatus = await indexerClient.GetIndexerStatusAsync(indexerName, cancellationToken);
            var lastResult = indexerStatus.Value.LastResult;
            if (lastResult is null
                || lastResult.Status != IndexerExecutionStatus.Success
                || lastResult.EndTime is null)
            {
                return false;
            }

            var indexName = indexerName.Replace("-indexer", "-index", StringComparison.Ordinal);
            var indexStats = await indexClient.GetIndexStatisticsAsync(indexName, cancellationToken);

            return indexStats.Value.DocumentCount >= expectedDocumentCount;
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

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.SearchEndpoint))
        {
            throw new InvalidOperationException("FoundryIqBootstrap:SearchEndpoint is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.StorageConnectionString))
        {
            throw new InvalidOperationException("FoundryIqBootstrap:StorageConnectionString is required.");
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
