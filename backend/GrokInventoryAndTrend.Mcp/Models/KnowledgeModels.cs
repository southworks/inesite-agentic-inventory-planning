namespace GrokInventoryAndTrend.Mcp.Models;

public sealed class KnowledgeEntry
{
    public required string KnowledgeRef { get; init; }

    public required string Rule { get; init; }

    public required string Threshold { get; init; }

    public required string Action { get; init; }

    public required string Exception { get; init; }

    public required string FullText { get; init; }
}

public sealed class KnowledgeMatch
{
    public required string KnowledgeRef { get; init; }

    public required string Rule { get; init; }

    public required string Threshold { get; init; }

    public required string Action { get; init; }

    public required string Exception { get; init; }

    public double Score { get; init; }
}

public sealed class GetRelevantKnowledgeResponse
{
    public required string Query { get; init; }

    public required IReadOnlyList<KnowledgeMatch> Entries { get; init; }
}
