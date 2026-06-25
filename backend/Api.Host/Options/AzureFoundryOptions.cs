namespace InventoryPlanning.Api.Host.Options;

public sealed class AzureFoundryOptions
{
    public const string SectionName = "AzureFoundry";

    public string ProjectEndpoint { get; set; } = string.Empty;

    public string SignalIngestionAgentName { get; set; } = "signal-ingestion-agent";

    public string FeatureCausalityAgentName { get; set; } = "feature-causality-agent";

    public string ForecastingAgentName { get; set; } = "forecasting-agent";

    public string ReplenishmentAllocationAgentName { get; set; } = "replenishment-allocation-agent";

    public string PlannerCopilotAgentName { get; set; } = "planner-copilot-agent";
}
