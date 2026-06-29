namespace GrokInventoryAndTrend.Mcp.Options;

public sealed class AzureFoundryModelsOptions
{
    public const string SectionName = "AzureFoundryModels";

    public string EmbedDeploymentName { get; set; } = "text-embedding-3-small";

    public string RerankDeploymentName { get; set; } = "Cohere-rerank-v4.0-fast";

    public string EmbedModelName { get; set; } = "text-embedding-3-small";

    public string RerankModelName { get; set; } = "Cohere-rerank-v4.0-fast";

    /// <summary>
    /// Hub deployment URL, e.g. https://{account}.services.ai.azure.com/openai/deployments/text-embedding-3-small
    /// </summary>
    public string EmbedEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Foundry account base URL, e.g. https://{account}.services.ai.azure.com
    /// </summary>
    public string RerankEndpoint { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public int EmbeddingDimensions { get; set; } = 1536;

    public int EmbeddingBatchSize { get; set; } = 16;

    public int MaxConcurrentEmbeddingRequests { get; set; } = 1;

    public int MaxConcurrentRerankRequests { get; set; } = 2;

    /// <summary>
    /// When true, retries transient Foundry HTTP failures including 429 throttling.
    /// </summary>
    public bool RetryEnabled { get; set; } = true;

    /// <summary>
    /// Maximum number of retry attempts after the initial request.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 4;

    /// <summary>
    /// Base delay in seconds for exponential backoff when Retry-After is not present.
    /// </summary>
    public double BaseDelaySeconds { get; set; } = 1;

    /// <summary>
    /// Maximum delay cap in seconds for exponential backoff retries.
    /// </summary>
    public double MaxDelaySeconds { get; set; } = 30;
}
