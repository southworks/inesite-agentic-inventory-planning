using CohereInventoryAndTrend.AgentProvisioning.Models;

namespace CohereInventoryAndTrend.AgentProvisioning;

public static class SettingsLoader
{
    public static ProvisioningSettings Load()
    {
        ProvisioningSettings settings = new()
        {
            ProjectEndpoint = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_PROJECT_ENDPOINT")?.Trim() ?? string.Empty,
            ModelDeploymentName = Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME")?.Trim() ?? string.Empty,
            McpBaseUrl = Environment.GetEnvironmentVariable("MCP_BASE_URL")?.Trim() ?? string.Empty
        };

        Validate(settings);
        return settings;
    }

    private static void Validate(ProvisioningSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ProjectEndpoint))
        {
            throw new InvalidOperationException(
                "AZURE_FOUNDRY_PROJECT_ENDPOINT is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.ModelDeploymentName))
        {
            throw new InvalidOperationException(
                "AZURE_AI_MODEL_DEPLOYMENT_NAME is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.McpBaseUrl))
        {
            throw new InvalidOperationException(
                "MCP_BASE_URL is required.");
        }

        if (!Uri.TryCreate(settings.ProjectEndpoint, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                $"AZURE_FOUNDRY_PROJECT_ENDPOINT '{settings.ProjectEndpoint}' is not a valid absolute URI.");
        }

        if (!Uri.TryCreate(settings.McpBaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                $"MCP_BASE_URL '{settings.McpBaseUrl}' is not a valid absolute URI.");
        }
    }
}
