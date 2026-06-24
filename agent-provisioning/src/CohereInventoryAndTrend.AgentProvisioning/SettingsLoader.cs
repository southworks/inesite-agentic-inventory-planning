using System.Text.Json;
using CohereInventoryAndTrend.AgentProvisioning.Models;
using Microsoft.Extensions.Configuration;

namespace CohereInventoryAndTrend.AgentProvisioning;

public static class SettingsLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static ProvisioningSettings Load(string? configPath = null)
    {
        string resolvedConfigPath = ResolveConfigPath(configPath);
        if (!File.Exists(resolvedConfigPath))
        {
            throw new InvalidOperationException(
                $"Provisioning configuration file was not found at '{resolvedConfigPath}'.");
        }

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile(resolvedConfigPath, optional: false)
            .AddEnvironmentVariables()
            .Build();

        ProvisioningSettings settings = configuration.Get<ProvisioningSettings>()
            ?? throw new InvalidOperationException(
                $"Provisioning configuration at '{resolvedConfigPath}' could not be parsed.");

        settings.ProjectEndpoint = FirstNonEmpty(
            settings.ProjectEndpoint,
            Environment.GetEnvironmentVariable("AZURE_FOUNDRY_PROJECT_ENDPOINT"),
            Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT"))
            ?? string.Empty;

        settings.ModelDeploymentName = FirstNonEmpty(
            settings.ModelDeploymentName,
            Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME"),
            Environment.GetEnvironmentVariable("ModelDeploymentName"))
            ?? "cohere-command-a";

        settings.McpBaseUrl = FirstNonEmpty(
            settings.McpBaseUrl,
            Environment.GetEnvironmentVariable("MCP_BASE_URL"))
            ?? string.Empty;

        Validate(settings);
        return settings;
    }

    public static string ResolveConfigPath(string? configPath)
    {
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            return Path.GetFullPath(configPath);
        }

        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "config", "provisioning.json"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config", "provisioning.json")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "agent-provisioning", "config", "provisioning.json")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "config", "provisioning.json"))
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates[^1];
    }

    private static void Validate(ProvisioningSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ProjectEndpoint))
        {
            throw new InvalidOperationException(
                "ProjectEndpoint is required. Set it in config/provisioning.json or AZURE_FOUNDRY_PROJECT_ENDPOINT.");
        }

        if (string.IsNullOrWhiteSpace(settings.ModelDeploymentName))
        {
            throw new InvalidOperationException("ModelDeploymentName is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.McpBaseUrl))
        {
            throw new InvalidOperationException(
                "McpBaseUrl is required. Set MCP_BASE_URL to the public MCP host URL.");
        }

        if (!Uri.TryCreate(settings.ProjectEndpoint, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                $"ProjectEndpoint '{settings.ProjectEndpoint}' is not a valid absolute URI.");
        }

        if (!Uri.TryCreate(settings.McpBaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                $"McpBaseUrl '{settings.McpBaseUrl}' is not a valid absolute URI.");
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
