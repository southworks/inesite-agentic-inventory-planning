namespace GrokInventoryAndTrend.Mcp.Governance;

/// <summary>
/// Maps MCP HTTP paths to role keys and stable agent DIDs for AGT policy evaluation.
/// </summary>
public static class McpEndpointIdentity
{
    public const string AgentIdClaimType = "agent_id";

    public static string ResolveServerKey(string path)
    {
        if (path.Contains("/signal-ingestion/", StringComparison.OrdinalIgnoreCase))
        {
            return "signal-ingestion";
        }

        if (path.Contains("/feature-and-causality/", StringComparison.OrdinalIgnoreCase))
        {
            return "feature-and-causality";
        }

        if (path.Contains("/forecasting/", StringComparison.OrdinalIgnoreCase))
        {
            return "forecasting";
        }

        if (path.Contains("/replenishment-and-allocation/", StringComparison.OrdinalIgnoreCase))
        {
            return "replenishment-and-allocation";
        }

        if (path.Contains("/planner-copilot/", StringComparison.OrdinalIgnoreCase))
        {
            return "planner-copilot";
        }

        return "signal-ingestion";
    }

    public static string ToAgentDid(string serverKey) => $"did:mcp:{serverKey}";

    public static string ResolveAgentDid(string path) =>
        ToAgentDid(ResolveServerKey(path));
}
