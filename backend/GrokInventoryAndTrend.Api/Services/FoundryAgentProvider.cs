using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Identity;
using GrokInventoryAndTrend.Api.Options;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace GrokInventoryAndTrend.Api.Services;

public sealed class FoundryAgents
{
    public required AIAgent SignalIngestion { get; init; }

    public required AIAgent FeatureCausality { get; init; }

    public required AIAgent Forecasting { get; init; }

    public required AIAgent ReplenishmentAllocation { get; init; }

    public required AIAgent PlannerCopilot { get; init; }
}

public sealed class FoundryAgentProvider
{
    private readonly AzureFoundryOptions _options;
    private readonly ILogger<FoundryAgentProvider> _logger;
    private readonly SemaphoreSlim _agentLoadLock = new(1, 1);
    private FoundryAgents? _agents;

    public FoundryAgentProvider(IOptions<AzureFoundryOptions> options, ILogger<FoundryAgentProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FoundryAgents> GetAgentsAsync(CancellationToken cancellationToken)
    {
        if (_agents is not null)
        {
            return _agents;
        }

        await _agentLoadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_agents is not null)
            {
                return _agents;
            }

            if (string.IsNullOrWhiteSpace(_options.ProjectEndpoint))
            {
                throw new InvalidOperationException(
                    "Azure Foundry configuration is missing. Set AzureFoundry:ProjectEndpoint in configuration or the AZURE_FOUNDRY_PROJECT_ENDPOINT environment variable.");
            }

            var credential = new DefaultAzureCredential();
            var projectEndpoint = new Uri(_options.ProjectEndpoint);
            var projectClient = new AIProjectClient(projectEndpoint, credential);
            var agentClient = new AgentAdministrationClient(projectEndpoint, credential);

            _logger.LogInformation("Resolving Azure AI Foundry agents from project endpoint {Endpoint}", _options.ProjectEndpoint);

            AIAgent signalIngestion = await LoadPromptAgentAsync(
                    projectClient,
                    agentClient,
                    _options.SignalIngestionAgentName,
                    cancellationToken)
                .ConfigureAwait(false);
            AIAgent featureCausality = await LoadPromptAgentAsync(
                    projectClient,
                    agentClient,
                    _options.FeatureCausalityAgentName,
                    cancellationToken)
                .ConfigureAwait(false);
            AIAgent forecasting = await LoadPromptAgentAsync(
                    projectClient,
                    agentClient,
                    _options.ForecastingAgentName,
                    cancellationToken)
                .ConfigureAwait(false);
            AIAgent replenishmentAllocation = await LoadPromptAgentAsync(
                    projectClient,
                    agentClient,
                    _options.ReplenishmentAllocationAgentName,
                    cancellationToken)
                .ConfigureAwait(false);
            AIAgent plannerCopilot = await LoadPromptAgentAsync(
                    projectClient,
                    agentClient,
                    _options.PlannerCopilotAgentName,
                    cancellationToken)
                .ConfigureAwait(false);

            _agents = new FoundryAgents
            {
                SignalIngestion = signalIngestion,
                FeatureCausality = featureCausality,
                Forecasting = forecasting,
                ReplenishmentAllocation = replenishmentAllocation,
                PlannerCopilot = plannerCopilot
            };

            return _agents;
        }
        finally
        {
            _agentLoadLock.Release();
        }
    }

    private async Task<AIAgent> LoadPromptAgentAsync(
        AIProjectClient projectClient,
        AgentAdministrationClient agentClient,
        string agentName,
        CancellationToken cancellationToken)
    {
        try
        {
            ProjectsAgentRecord agentRecord = (await agentClient
                    .GetAgentAsync(agentName, cancellationToken)
                    .ConfigureAwait(false))
                .Value;

            AIAgent agent = projectClient.AsAIAgent(agentRecord);

            _logger.LogInformation(
                "Resolved Foundry prompt agent {AgentName} (record {AgentId}) as {AgentType}.",
                agentName,
                agentRecord.Id,
                agent.GetType().Name);

            return agent;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Required Foundry prompt agent '{agentName}' could not be resolved. Verify the agent exists in the project and that the caller is authenticated.",
                ex);
        }
    }
}
