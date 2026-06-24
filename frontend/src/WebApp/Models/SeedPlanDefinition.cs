using Cohere.InventoryAndTrend.WebApp.Contracts;

namespace Cohere.InventoryAndTrend.WebApp.Models;

public sealed class SeedPlanDefinition
{
    public string ScenarioId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public ScenarioOutcomeTag OutcomeTag { get; set; }

    public PlanContext Context { get; set; } = new();

    public string ScenarioFolder { get; set; } = string.Empty;

    public string ExecutionProfile { get; set; } = "default";
}
