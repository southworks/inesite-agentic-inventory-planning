using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;

namespace GrokInventoryAndTrend.Mcp.Startup;

internal sealed class PolicyEmbeddingClient : IDisposable
{
    private static readonly string[] Scopes = ["https://cognitiveservices.azure.com/.default"];

    private readonly HttpClient _httpClient;
    private readonly Uri _embeddingsUri;
    private readonly TokenCredential _credential;
    private readonly ILogger _logger;

    public PolicyEmbeddingClient(
        string foundryResourceUri,
        string embedDeploymentName,
        TokenCredential credential,
        ILogger logger)
    {
        _credential = credential;
        _logger = logger;
        _httpClient = new HttpClient();
        _embeddingsUri = new Uri(
            $"{foundryResourceUri.TrimEnd('/')}/openai/deployments/{embedDeploymentName}/embeddings?api-version=2024-10-21");
    }

    public async Task<IReadOnlyList<float>> EmbedAsync(string input, CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(new TokenRequestContext(Scopes), cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, _embeddingsUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Content = JsonContent.Create(new EmbeddingRequest
        {
            Input = input,
            Model = "text-embedding-3-small"
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Embedding request failed. Status={StatusCode}, Uri={Uri}, Body={Body}",
                (int)response.StatusCode,
                _embeddingsUri,
                responseBody);
            response.EnsureSuccessStatusCode();
        }

        var payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Embedding response was empty.");

        if (payload.Data.Count == 0 || payload.Data[0].Embedding.Count == 0)
        {
            throw new InvalidOperationException("Embedding response did not contain vector data.");
        }

        return payload.Data[0].Embedding;
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class EmbeddingRequest
    {
        [JsonPropertyName("input")]
        public string Input { get; set; } = string.Empty;

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;
    }

    private sealed class EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingData> Data { get; set; } = [];
    }

    private sealed class EmbeddingData
    {
        [JsonPropertyName("embedding")]
        public List<float> Embedding { get; set; } = [];
    }
}
