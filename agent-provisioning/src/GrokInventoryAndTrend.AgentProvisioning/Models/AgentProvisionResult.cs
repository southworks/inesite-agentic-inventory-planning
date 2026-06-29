namespace GrokInventoryAndTrend.AgentProvisioning.Models;

public enum ProvisionOutcome
{
    Created,
    Updated,
    Unchanged,
    Failed
}

public sealed class AgentProvisionResult
{
    public required string AgentName { get; init; }

    public required ProvisionOutcome Outcome { get; init; }

    public required string Message { get; init; }
}
