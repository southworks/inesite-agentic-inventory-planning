using GrokInventoryAndTrend.AgentProvisioning.Models;

namespace GrokInventoryAndTrend.AgentProvisioning;

internal static class Program
{
    private static async Task<int> Main()
    {
        try
        {
            ProvisioningSettings settings = SettingsLoader.Load();
            AgentAssetLoader assetLoader = new(AgentAssetLoader.ResolveAgentsRoot(null));
            IReadOnlyList<AgentAssetBundle> bundles = assetLoader.LoadAll();

            Console.WriteLine($"Provisioning {bundles.Count} agents to {settings.ProjectEndpoint}");
            Console.WriteLine($"Model deployment: {settings.ModelDeploymentName}");
            Console.WriteLine($"MCP base URL: {settings.McpBaseUrl}");

            FoundryAgentProvisioner provisioner = new();
            IReadOnlyList<AgentProvisionResult> results =
                await provisioner.ProvisionAllAsync(settings, bundles, CancellationToken.None).ConfigureAwait(false);

            foreach (AgentProvisionResult result in results)
            {
                Console.WriteLine($"{result.Outcome,-10} {result.AgentName} - {result.Message}");
            }

            Console.WriteLine("Agent provisioning completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Agent provisioning failed: {ex.Message}");
            return 1;
        }
    }
}
