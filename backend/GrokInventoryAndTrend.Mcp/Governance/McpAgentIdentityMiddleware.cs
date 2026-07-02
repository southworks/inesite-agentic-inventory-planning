using System.Security.Claims;

namespace GrokInventoryAndTrend.Mcp.Governance;

/// <summary>
/// Assigns a stable AGT agent identity from the MCP endpoint path before tool governance runs.
/// </summary>
public sealed class McpAgentIdentityMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.Contains("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            var agentDid = McpEndpointIdentity.ResolveAgentDid(path);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(McpEndpointIdentity.AgentIdClaimType, agentDid),
                    new Claim(ClaimTypes.NameIdentifier, agentDid),
                ],
                authenticationType: "mcp-endpoint"));
        }

        return next(context);
    }
}
