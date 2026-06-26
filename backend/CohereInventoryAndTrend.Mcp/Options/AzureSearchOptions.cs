namespace CohereInventoryAndTrend.Mcp.Options;

public sealed class AzureSearchOptions
{
    public const string SectionName = "AzureSearch";

    public string Endpoint { get; set; } = string.Empty;

    public string EvidenceIndexName { get; set; } = "inventory-signal-evidence";

    public int VectorDimensions { get; set; } = 1536;
}
