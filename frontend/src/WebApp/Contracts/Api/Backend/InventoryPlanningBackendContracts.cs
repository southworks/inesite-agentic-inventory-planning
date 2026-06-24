using System.Text.Json.Serialization;

namespace Cohere.InventoryAndTrend.WebApp.Contracts.Api.Backend;

/// <summary>
/// Wire DTOs matching the provisional backend contract.
/// Update these when the backend team delivers their OpenAPI spec.
/// </summary>
public sealed class BackendStartWorkflowResponse
{
    [JsonPropertyName("executionId")]
    public string ExecutionId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

public sealed class BackendWorkflowStatusResponse
{
    [JsonPropertyName("planId")]
    public string PlanId { get; set; } = string.Empty;

    [JsonPropertyName("executionId")]
    public string ExecutionId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("currentStage")]
    public string? CurrentStage { get; set; }

    [JsonPropertyName("statusMessage")]
    public string StatusMessage { get; set; } = string.Empty;

    [JsonPropertyName("stages")]
    public IReadOnlyList<BackendStageStatus> Stages { get; set; } = [];
}

public sealed class BackendStageStatus
{
    [JsonPropertyName("stageKey")]
    public string StageKey { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("outputJson")]
    public string? OutputJson { get; set; }

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; set; }
}
