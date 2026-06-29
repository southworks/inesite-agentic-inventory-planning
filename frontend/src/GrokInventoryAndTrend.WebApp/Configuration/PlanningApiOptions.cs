namespace GrokInventoryAndTrend.WebApp.Configuration;

public sealed class PlanningApiOptions
{
    public const string SectionName = "PlanningApi";

    public string BaseUrl { get; set; } = "http://localhost:5038/";
}
