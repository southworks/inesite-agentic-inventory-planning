using Azure;
using Azure.AI.Projects.Agents;
using Azure.Identity;
using GrokInventoryAndTrend.AgentProvisioning.Models;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;

namespace GrokInventoryAndTrend.AgentProvisioning;

public sealed class FoundryAgentProvisioner
{
    private readonly AgentDefinitionBuilder _definitionBuilder = new();

    public async Task<IReadOnlyList<AgentProvisionResult>> ProvisionAllAsync(
        ProvisioningSettings settings,
        IReadOnlyList<AgentAssetBundle> bundles,
        CancellationToken cancellationToken)
    {
        AgentAdministrationClient agentClient = new(
            new Uri(settings.ProjectEndpoint),
            new DefaultAzureCredential());

        List<AgentProvisionResult> results = [];
        foreach (AgentAssetBundle bundle in bundles.OrderBy(item => item.Manifest.Name, StringComparer.OrdinalIgnoreCase))
        {
            results.Add(await ProvisionAgentAsync(agentClient, settings, bundle, cancellationToken)
                .ConfigureAwait(false));
        }

        if (results.Any(result => result.Outcome == ProvisionOutcome.Failed))
        {
            string details = string.Join("; ", results
                .Where(result => result.Outcome == ProvisionOutcome.Failed)
                .Select(result => $"{result.AgentName}: {result.Message}"));
            throw new InvalidOperationException($"One or more agents failed to provision. {details}");
        }

        return results;
    }

    private async Task<AgentProvisionResult> ProvisionAgentAsync(
        AgentAdministrationClient agentClient,
        ProvisioningSettings settings,
        AgentAssetBundle bundle,
        CancellationToken cancellationToken)
    {
        string agentName = bundle.Manifest.Name;

        try
        {
            string definitionJson = _definitionBuilder.BuildDefinitionJson(bundle, settings);
            string desiredFingerprint = _definitionBuilder.ComputeFingerprint(definitionJson);

            ProjectsAgentVersion? existingVersion =
                await TryGetLatestAgentVersionAsync(agentClient, agentName, cancellationToken).ConfigureAwait(false);

            if (existingVersion is not null)
            {
                string existingDefinitionJson = JsonSerializer.Serialize(existingVersion.Definition);
                string existingFingerprint = _definitionBuilder.ComputeFingerprint(existingDefinitionJson);
                if (string.Equals(existingFingerprint, desiredFingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    return new AgentProvisionResult
                    {
                        AgentName = agentName,
                        Outcome = ProvisionOutcome.Unchanged,
                        Message = $"Agent '{agentName}' version {existingVersion.Version} is already up to date."
                    };
                }
            }

            string requestJson = _definitionBuilder.BuildCreateVersionRequestJson(definitionJson, bundle.Manifest);
            await agentClient.CreateAgentVersionAsync(
                agentName,
                BinaryContent.Create(BinaryData.FromString(requestJson)),
                foundryFeatures: null,
                options: new RequestOptions { CancellationToken = cancellationToken }).ConfigureAwait(false);

            ProvisionOutcome outcome = existingVersion is null ? ProvisionOutcome.Created : ProvisionOutcome.Updated;
            return new AgentProvisionResult
            {
                AgentName = agentName,
                Outcome = outcome,
                Message = $"Agent '{agentName}' was {(outcome == ProvisionOutcome.Created ? "created" : "updated")}."
            };
        }
        catch (Exception ex)
        {
            return new AgentProvisionResult
            {
                AgentName = agentName,
                Outcome = ProvisionOutcome.Failed,
                Message = ex.Message
            };
        }
    }

    private static async Task<ProjectsAgentVersion?> TryGetLatestAgentVersionAsync(
        AgentAdministrationClient agentClient,
        string agentName,
        CancellationToken cancellationToken)
    {
        try
        {
            ClientResult<ProjectsAgentVersion> response = await agentClient
                .GetAgentVersionAsync(agentName, "latest", cancellationToken)
                .ConfigureAwait(false);

            return response.Value;
        }
        catch (Exception ex) when (IsMissingAgentVersion(ex))
        {
            return null;
        }
    }

    private static bool IsMissingAgentVersion(Exception exception)
    {
        if (exception is RequestFailedException { Status: 404 })
        {
            return true;
        }

        return exception.Message.Contains("agentVersion{latest}", StringComparison.OrdinalIgnoreCase);
    }
}
