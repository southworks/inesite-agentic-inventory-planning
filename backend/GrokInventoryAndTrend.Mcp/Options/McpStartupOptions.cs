namespace GrokInventoryAndTrend.Mcp.Options;

public sealed class McpStartupOptions
{
    public const string SectionName = "McpStartup";

    /// <summary>
    /// When true, ensures Azure AI Search index schemas exist during MCP host startup.
    /// </summary>
    public bool EnsureSearchIndexesOnStartup { get; set; } = true;

    /// <summary>
    /// When true, seeds the policy index during MCP host startup. Disabled by default so
    /// deploy-time seed jobs or <c>--seed-policies</c> handle indexing instead.
    /// </summary>
    public bool SeedPoliciesOnStartup { get; set; }
}
