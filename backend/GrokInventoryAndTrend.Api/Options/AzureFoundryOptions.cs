namespace GrokInventoryAndTrend.Api.Options;

public sealed class AzureFoundryOptions
{
    public const string SectionName = "AzureFoundry";

    public string ProjectEndpoint { get; set; } = string.Empty;

    public string SignalIngestionAgentName { get; set; } = "signal-ingestion-agent";

    public string FeatureCausalityAgentName { get; set; } = "feature-and-causality-agent";

    public string ForecastingAgentName { get; set; } = "forecasting-agent";

    public string ReplenishmentAllocationAgentName { get; set; } = "replenishment-and-allocation-agent";

    public string PlannerCopilotAgentName { get; set; } = "planner-copilot-agent";
}
