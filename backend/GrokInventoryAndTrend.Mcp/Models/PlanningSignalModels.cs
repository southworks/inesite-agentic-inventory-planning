using System.Text.Json;

namespace GrokInventoryAndTrend.Mcp.Models;

public sealed class PlanningSignalDocument
{
    public required string DocumentId { get; init; }

    public required string DocumentType { get; init; }

    public required string Category { get; init; }

    public required string SourcePath { get; init; }

    public required JsonElement Content { get; init; }

    public string SummaryText { get; init; } = string.Empty;
}

public sealed class GetPlanningSignalsResponse
{
    public required string CaseId { get; init; }

    public required string ExecutionId { get; init; }

    public required IReadOnlyList<PlanningSignalDocument> Signals { get; init; }

    public required IReadOnlyList<string> AvailableCategories { get; init; }

    public required IReadOnlyList<string> MissingCategories { get; init; }

    public string Source { get; init; } = "local-demo-assets";
}
