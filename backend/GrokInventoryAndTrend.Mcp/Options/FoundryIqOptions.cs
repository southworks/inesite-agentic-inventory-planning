namespace GrokInventoryAndTrend.Mcp.Options;

public sealed class FoundryIqOptions
{
    public const string SectionName = "FoundryIq";

    public string SearchEndpoint { get; set; } = string.Empty;

    public string PolicyKnowledgeBaseName { get; set; } = "inventory-policy-knowledge-kb";

    public string PolicyKnowledgeSourceName { get; set; } = "inventory-policy-knowledge-ks";

    public int DefaultMaxOutputDocuments { get; set; } = 10;

    public int MaxOutputSizeInTokens { get; set; } = 8000;
}
