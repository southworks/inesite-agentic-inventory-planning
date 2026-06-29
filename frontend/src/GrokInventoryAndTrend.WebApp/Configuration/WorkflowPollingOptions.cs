namespace GrokInventoryAndTrend.WebApp.Configuration;

public sealed class WorkflowPollingOptions
{
    public const string SectionName = "WorkflowPolling";

    public int IntervalSeconds { get; set; } = 2;

    public int MaxDurationMinutes { get; set; } = 10;

    public int TraceRefreshEveryNTicks { get; set; } = 3;
}
