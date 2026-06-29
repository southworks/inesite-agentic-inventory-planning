using Cohere.InventoryAndTrend.WebApp.Contracts;

namespace Cohere.InventoryAndTrend.WebApp.Models;

public static class PlanDisplayFormatting
{
    public static string FormatCurrency(decimal amount) => amount.ToString("C0");

    public static string FormatFillRate(decimal rate) => $"{rate:P0}";

    public static string FormatPlanMeta(string planId, string? executionId)
    {
        return executionId is null
            ? $"Case {planId}"
            : $"Case {planId} · Run {executionId[..Math.Min(8, executionId.Length)]}";
    }
}

public static class StageExplainabilityUi
{
    public static string DescribeStageStatus(string status) => status switch
    {
        "Completed" => "Completed",
        "Running" => "Running",
        "Pending" => "Pending",
        _ => status
    };

    public static string ToStageStatusClass(string status) => status switch
    {
        "Completed" => "stage-status--completed",
        "Running" => "stage-status--running",
        _ => "stage-status--pending"
    };
}
