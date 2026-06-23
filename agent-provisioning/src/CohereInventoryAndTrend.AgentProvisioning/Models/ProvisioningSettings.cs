namespace CohereInventoryAndTrend.AgentProvisioning.Models;

public sealed class ProvisioningSettings
{
    public string ProjectEndpoint { get; set; } = string.Empty;

    public string ModelDeploymentName { get; set; } = "cohere-command-a";

    public string McpBaseUrl { get; set; } = string.Empty;
}
