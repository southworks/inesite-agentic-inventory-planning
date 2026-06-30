namespace GrokInventoryAndTrend.Mcp.Options;

public sealed class FoundryIqBootstrapOptions
{
    public const string SectionName = "FoundryIqBootstrap";

    public string SearchEndpoint { get; set; } = string.Empty;

    public string PolicyIndexName { get; set; } = "inventory-policy-knowledge";

    public string PolicyKnowledgeSourceName { get; set; } = "inventory-policy-knowledge-ks";

    public string PolicyKnowledgeBaseName { get; set; } = "inventory-policy-knowledge-kb";

    public string FoundryResourceUri { get; set; } = string.Empty;

    public string EmbedDeploymentName { get; set; } = "text-embedding-3-small";

    public string EmbedModelName { get; set; } = "text-embedding-3-small";

    public int EmbeddingDimensions { get; set; } = 1536;

    public string SemanticConfigurationName { get; set; } = "policy-semantic-config";

    public string PolicyFilePath { get; set; } = string.Empty;

    public int IndexerPollAttempts { get; set; } = 120;

    public int IndexerPollDelaySeconds { get; set; } = 20;
}
