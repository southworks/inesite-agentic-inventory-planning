namespace Cohere.InventoryAndTrend.WebApp.Configuration;

public sealed class PlanningApiOptions
{
    public const string SectionName = "PlanningApi";

    public string Mode { get; set; } = "Local";

    public string BaseUrl { get; set; } = "http://localhost:5038/";

    public bool IsRemote => string.Equals(Mode, "Remote", StringComparison.OrdinalIgnoreCase);
}
