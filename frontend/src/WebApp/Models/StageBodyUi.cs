namespace Grok.InventoryAndTrend.WebApp.Models;

public enum StageBodyDisplay
{
    Panel,
    Running,
    Pending
}

public static class StageBodyUi
{
    public static StageBodyDisplay Resolve(string? stageStatus, bool hasOutput)
    {
        if (hasOutput)
        {
            return StageBodyDisplay.Panel;
        }

        if (string.Equals(stageStatus, "Running", StringComparison.OrdinalIgnoreCase))
        {
            return StageBodyDisplay.Running;
        }

        return StageBodyDisplay.Pending;
    }
}
