namespace GrokInventoryAndTrend.Mcp.Options;

public sealed class DatasetOptions
{
    public const string SectionName = "Dataset";

    public string RootPath { get; set; } = string.Empty;

    public string CasesRelativePath { get; set; } = "cases";

    public string FabricPrerequisiteSubfolder { get; set; } = "fabric-pre-requisite-data";

    public string PromotionsFilePath { get; set; } = string.Empty;

    public string SignalQualityFilePath { get; set; } = string.Empty;

    public string TrendPatternsFilePath { get; set; } = string.Empty;

    public string PlanningConstraintsFilePath { get; set; } = string.Empty;
}
