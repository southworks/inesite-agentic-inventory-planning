using GrokInventoryAndTrend.AgentProvisioning.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GrokInventoryAndTrend.AgentProvisioning;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
        });

        builder.Services.AddSingleton<FoundryAgentProvisioner>();

        using IHost host = builder.Build();
        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AgentProvisioning");
        FoundryAgentProvisioner provisioner = host.Services.GetRequiredService<FoundryAgentProvisioner>();

        try
        {
            ProvisioningSettings settings = SettingsLoader.Load();
            AgentAssetLoader assetLoader = new(AgentAssetLoader.ResolveAgentsRoot(null));
            IReadOnlyList<AgentAssetBundle> bundles = assetLoader.LoadAll();

            logger.LogInformation(
                "Provisioning {AgentCount} agents to {ProjectEndpoint}.",
                bundles.Count,
                settings.ProjectEndpoint);

            IReadOnlyList<AgentProvisionResult> results =
                await provisioner.ProvisionAllAsync(settings, bundles, CancellationToken.None).ConfigureAwait(false);

            foreach (AgentProvisionResult result in results)
            {
                LogLevel level = result.Outcome == ProvisionOutcome.Failed ? LogLevel.Error : LogLevel.Information;

                logger.Log(
                    level,
                    "{Outcome} {AgentName} - {Message}",
                    result.Outcome,
                    result.AgentName,
                    result.Message);
            }

            logger.LogInformation("Agent provisioning completed successfully.");

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent provisioning failed. {Details}", AzureServiceExceptionDetails.Describe(ex));
            return 1;
        }
    }
}
