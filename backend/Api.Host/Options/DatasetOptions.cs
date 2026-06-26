namespace InventoryPlanning.Api.Host.Options;

public sealed class DatasetOptions
{
    public const string SectionName = "Dataset";

    public string RootPath { get; set; } = string.Empty;

    public string CasesRelativePath { get; set; } = "cases";

    public string IngestSubfolder { get; set; } = "ingest";
}
