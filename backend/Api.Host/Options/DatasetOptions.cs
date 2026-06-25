namespace InventoryPlanning.Api.Host.Options;

public sealed class DatasetOptions
{
    public const string SectionName = "Dataset";

    public string RootPath { get; set; } = string.Empty;

    public string RawTextRelativePath { get; set; } = "00_raw/txt";
}
