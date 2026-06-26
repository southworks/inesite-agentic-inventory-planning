namespace Cohere.InventoryAndTrend.WebApp.Configuration;

public sealed class DatasetSeedOptions
{
    public const string SectionName = "DatasetSeed";

    public string RootPath { get; set; } = "../../../dataset-seed";
}
