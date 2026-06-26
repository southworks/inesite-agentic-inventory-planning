namespace CohereInventoryAndTrend.Mcp.Options;

public enum DataSourceMode
{
    Local = 0,
    Fabric = 1
}

public sealed class DataSourceOptions
{
    public const string SectionName = "DataSource";

    public DataSourceMode Mode { get; set; } = DataSourceMode.Local;
    public FabricLakehouseOptions? FabricLakehouse { get; set; }
}

public sealed class FabricLakehouseOptions
{
    public string WorkspaceName { get; set; } = string.Empty;
    public string LakehouseName { get; set; } = string.Empty;
    public string EvidenceRoot { get; set; } = "Files/bronze";
    public int TimeoutSeconds { get; set; } = 30;
}
