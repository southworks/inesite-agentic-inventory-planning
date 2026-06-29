namespace Grok.InventoryAndTrend.WebApp.Contracts;

public class AgentStageResult
{
    public string Summary { get; set; } = string.Empty;

    public string Decision { get; set; } = string.Empty;

    public string Evidence { get; set; } = string.Empty;
}

public sealed class ForecastingStageResult : AgentStageResult
{
    public string ConfidenceLevel { get; set; } = string.Empty;

    public IReadOnlyList<string> Anomalies { get; set; } = [];

    public IReadOnlyList<string> KeyMetrics { get; set; } = [];
}

public sealed class PlannerCopilotStageResult : AgentStageResult
{
    public string ApprovalAssessment { get; set; } = string.Empty;

    public string BudgetImpact { get; set; } = string.Empty;

    public string ServiceLevelImpact { get; set; } = string.Empty;

    public IReadOnlyList<string> Concerns { get; set; } = [];

    public IReadOnlyList<string> Recommendations { get; set; } = [];
}

public sealed class ReplenishmentLineItem
{
    public string Sku { get; set; } = string.Empty;

    public string OrderType { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public string Location { get; set; } = string.Empty;

    public string Eta { get; set; } = string.Empty;
}

public sealed class ReplenishmentStageResult : AgentStageResult
{
    public IReadOnlyList<ReplenishmentLineItem> LineItems { get; set; } = [];
}

public sealed class SignalIngestionStageResult : AgentStageResult
{
    public IReadOnlyList<string> SourcesIngested { get; set; } = [];

    public IReadOnlyList<string> QualityFlags { get; set; } = [];
}

public sealed class FeatureCausalityStageResult : AgentStageResult
{
    public IReadOnlyList<string> TopDrivers { get; set; } = [];

    public IReadOnlyList<string> ElasticityNotes { get; set; } = [];
}
