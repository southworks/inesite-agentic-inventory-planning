namespace CohereInventoryAndTrend.Mcp.Options;

public sealed class DatasetOptions
{
    public const string SectionName = "Dataset";

    public string RootPath { get; set; } = string.Empty;

    public string PolicyFilePath { get; set; } = string.Empty;

    public string PromotionsFilePath { get; set; } = string.Empty;

    public string SignalQualityFilePath { get; set; } = string.Empty;

    public string TrendPatternsFilePath { get; set; } = string.Empty;

    public string PlanningConstraintsFilePath { get; set; } = string.Empty;
}
