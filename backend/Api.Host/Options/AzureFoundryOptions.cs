namespace InventoryPlanning.Api.Host.Options;

public sealed class AzureFoundryOptions
{
    public const string SectionName = "AzureFoundry";

    public string ProjectEndpoint { get; set; } = string.Empty;

    public string DocumentProcessingAgentName { get; set; } = "document-processing-agent";

    public string PlanningReviewAgentName { get; set; } = "planning-review-agent";

    public string ResponsibleAiAgentName { get; set; } = "responsible-ai-agent";

    public string PlanExecutionAgentName { get; set; } = "plan-execution-agent";
}
