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
                "Starting agent provisioning. AgentCount={AgentCount}, ProjectEndpoint={ProjectEndpoint}, ModelDeployment={ModelDeployment}, McpBaseUrl={McpBaseUrl}",
                bundles.Count,
                settings.ProjectEndpoint,
                settings.ModelDeploymentName,
                settings.McpBaseUrl);

            IReadOnlyList<AgentProvisionResult> results =
                await provisioner.ProvisionAllAsync(settings, bundles, CancellationToken.None).ConfigureAwait(false);

            foreach (AgentProvisionResult result in results)
            {
                LogLevel level = result.Outcome switch
                {
                    ProvisionOutcome.Failed => LogLevel.Error,
                    ProvisionOutcome.Created or ProvisionOutcome.Updated => LogLevel.Information,
                    _ => LogLevel.Information
                };

                logger.Log(
                    level,
                    "Agent {AgentName}: {Outcome}. {Message}",
                    result.AgentName,
                    result.Outcome,
                    result.Message);
            }

            int created = results.Count(result => result.Outcome == ProvisionOutcome.Created);
            int updated = results.Count(result => result.Outcome == ProvisionOutcome.Updated);
            int unchanged = results.Count(result => result.Outcome == ProvisionOutcome.Unchanged);

            logger.LogInformation(
                "Agent provisioning completed successfully. Created={Created}, Updated={Updated}, Unchanged={Unchanged}",
                created,
                updated,
                unchanged);

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent provisioning failed. {Details}", AzureServiceExceptionDetails.Describe(ex));
            return 1;
        }
    }
}
