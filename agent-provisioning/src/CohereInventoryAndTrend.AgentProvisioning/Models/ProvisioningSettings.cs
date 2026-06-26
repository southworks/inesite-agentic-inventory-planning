namespace CohereInventoryAndTrend.AgentProvisioning.Models;

public sealed class ProvisioningSettings
{
    public string ProjectEndpoint { get; set; } = string.Empty;

    public string ModelDeploymentName { get; set; } = "grok-4.3";

    public string McpBaseUrl { get; set; } = string.Empty;
}
