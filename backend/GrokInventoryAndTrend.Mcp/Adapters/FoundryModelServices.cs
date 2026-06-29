using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using GrokInventoryAndTrend.Mcp.Options;
using Microsoft.Extensions.Options;

namespace GrokInventoryAndTrend.Mcp.Adapters;

public sealed class FoundryEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly AzureFoundryModelsOptions _options;
    private readonly ILogger<FoundryEmbeddingService> _logger;
    private readonly DefaultAzureCredential _credential = new();
    private readonly SemaphoreSlim _requestGate;

    public FoundryEmbeddingService(
        HttpClient httpClient,
        IOptions<AzureFoundryModelsOptions> options,
        ILogger<FoundryEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _requestGate = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentEmbeddingRequests));
    }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        if (texts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Embedding input must not contain empty or whitespace-only strings.", nameof(texts));
        }

        var batchSize = Math.Max(1, _options.EmbeddingBatchSize);
        var embeddings = new List<float[]>(texts.Count);

        foreach (var batch in texts.Chunk(batchSize))
        {
            embeddings.AddRange(await EmbedBatchAsync(batch, cancellationToken));
        }

        return embeddings;
    }

    private async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        ValidateEndpoint(_options.EmbedEndpoint, "EmbedEndpoint");

        var requestUrl = FoundryEndpointBuilder.BuildEmbeddingsUrl(_options);
        _logger.LogDebug("Sending Foundry embed request with {InputCount} input(s) to {RequestUrl}", texts.Count, requestUrl);

        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            await ApplyAuthorizationAsync(request, cancellationToken);
            request.Content = FoundryHttpExtensions.CreateJsonContent(new AzureEmbedRequest
            {
                Model = _options.EmbedModelName,
                Input = texts,
                Dimensions = _options.EmbeddingDimensions
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await response.EnsureFoundrySuccessAsync("embed", cancellationToken);

            var payload = await response.Content.ReadFromJsonAsync<AzureEmbedResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Azure Foundry embed response was empty.");

            return payload.Data
                .OrderBy(item => item.Index)
                .Select(item => item.Embedding.ToArray())
                .ToArray();
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task ApplyAuthorizationAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            return;
        }

        AccessToken token = await _credential.GetTokenAsync(
            new TokenRequestContext(["https://ai.azure.com/.default"]),
            cancellationToken);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
    }

    private static void ValidateEndpoint(string endpoint, string settingName)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException($"AzureFoundryModels:{settingName} is required.");
        }
    }

    private sealed class AzureEmbedRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("input")]
        public required IReadOnlyList<string> Input { get; init; }

        [JsonPropertyName("dimensions")]
        public required int Dimensions { get; init; }
    }

    private sealed class AzureEmbedResponse
    {
        [JsonPropertyName("data")]
        public required IReadOnlyList<AzureEmbedResponseItem> Data { get; init; }
    }

    private sealed class AzureEmbedResponseItem
    {
        [JsonPropertyName("index")]
        public int Index { get; init; }

        [JsonPropertyName("embedding")]
        public required IReadOnlyList<float> Embedding { get; init; }
    }
}

public sealed class FoundryRerankService
{
    private readonly HttpClient _httpClient;
    private readonly AzureFoundryModelsOptions _options;
    private readonly ILogger<FoundryRerankService> _logger;
    private readonly DefaultAzureCredential _credential = new();
    private readonly SemaphoreSlim _requestGate;

    public FoundryRerankService(
        HttpClient httpClient,
        IOptions<AzureFoundryModelsOptions> options,
        ILogger<FoundryRerankService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _requestGate = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentRerankRequests));
    }

    public async Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query,
        IReadOnlyList<string> documents,
        int topK,
        CancellationToken cancellationToken = default)
    {
        if (documents.Count == 0)
        {
            return [];
        }

        ValidateEndpoint(_options.RerankEndpoint, "RerankEndpoint");

        var requestUrl = FoundryEndpointBuilder.BuildRerankUrl(_options);
        _logger.LogDebug("Sending Foundry rerank request to {RequestUrl}", requestUrl);

        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            await ApplyAuthorizationAsync(request, cancellationToken);
            request.Content = FoundryHttpExtensions.CreateJsonContent(new RerankRequest
            {
                Model = ResolveRerankModelName(_options),
                Query = query,
                Documents = documents,
                TopN = topK
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await response.EnsureFoundrySuccessAsync("rerank", cancellationToken);

            var payload = await response.Content.ReadFromJsonAsync<RerankResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Azure Foundry rerank response was empty.");

            return payload.Results
                .Select(result => new RerankResult(result.Index, result.RelevanceScore))
                .ToArray();
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task ApplyAuthorizationAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            return;
        }

        AccessToken token = await _credential.GetTokenAsync(
            new TokenRequestContext(["https://ai.azure.com/.default"]),
            cancellationToken);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
    }

    private static void ValidateEndpoint(string endpoint, string settingName)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException($"AzureFoundryModels:{settingName} is required.");
        }
    }

    private static string ResolveRerankModelName(AzureFoundryModelsOptions options)
    {
        // Foundry's /providers/cohere/v2/rerank expects the deployment name, not the catalog model id.
        return string.IsNullOrWhiteSpace(options.RerankDeploymentName)
            ? options.RerankModelName
            : options.RerankDeploymentName;
    }

    public sealed record RerankResult(int Index, double Score);

    private sealed class RerankRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("query")]
        public required string Query { get; init; }

        [JsonPropertyName("documents")]
        public required IReadOnlyList<string> Documents { get; init; }

        [JsonPropertyName("top_n")]
        public required int TopN { get; init; }
    }

    private sealed class RerankResponse
    {
        [JsonPropertyName("results")]
        public required IReadOnlyList<RerankResponseItem> Results { get; init; }
    }

    private sealed class RerankResponseItem
    {
        [JsonPropertyName("index")]
        public int Index { get; init; }

        [JsonPropertyName("relevance_score")]
        public double RelevanceScore { get; init; }
    }
}
