namespace GrokInventoryAndTrend.Mcp.Tools;

internal static class DemoToolDefaults
{
    internal const int DefaultTopK = 5;

    internal static string CaseQuery(string caseId, string purpose) =>
        $"{purpose} for inventory planning case {caseId}.";

    internal static string CaseContext(string caseId, string executionId) =>
        $"caseId={caseId}; executionId={executionId}";
}
