namespace CohereInventoryAndTrend.Mcp.Options;

public sealed class McpStartupOptions
{
    public const string SectionName = "McpStartup";

    /// <summary>
    /// When true, ensures Azure AI Search index schemas exist during MCP host startup.
    /// </summary>
    public bool EnsureSearchIndexesOnStartup { get; set; } = true;
}
