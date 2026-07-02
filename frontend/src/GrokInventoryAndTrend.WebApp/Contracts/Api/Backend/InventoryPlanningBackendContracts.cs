using System.Text.Json.Serialization;

namespace GrokInventoryAndTrend.WebApp.Contracts.Api.Backend;

/// <summary>
/// Wire DTOs matching backend/GrokInventoryAndTrend.Api/Contracts/InventoryPlanningApiContracts.cs
/// </summary>
public sealed class BackendBasicWorkflowStatusResponse
{
    [JsonPropertyName("executionId")]
    public string ExecutionId { get; set; } = string.Empty;

    [JsonPropertyName("caseId")]
    public string CaseId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("agentOutputs")]
    public BackendBasicWorkflowAgentOutputsResponse AgentOutputs { get; set; } = new();

    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; set; }

    [JsonPropertyName("lastUpdatedUtc")]
    public DateTimeOffset LastUpdatedUtc { get; set; }
}

public sealed class BackendBasicWorkflowAgentOutputsResponse
{
    [JsonPropertyName("signalIngestion")]
    [JsonConverter(typeof(BackendAgentOutputJsonConverter))]
    public string? SignalIngestion { get; set; }

    [JsonPropertyName("featureCausality")]
    [JsonConverter(typeof(BackendAgentOutputJsonConverter))]
    public string? FeatureCausality { get; set; }

    [JsonPropertyName("forecasting")]
    [JsonConverter(typeof(BackendAgentOutputJsonConverter))]
    public string? Forecasting { get; set; }

    [JsonPropertyName("replenishmentAllocation")]
    [JsonConverter(typeof(BackendAgentOutputJsonConverter))]
    public string? ReplenishmentAllocation { get; set; }

    [JsonPropertyName("plannerCopilot")]
    [JsonConverter(typeof(BackendAgentOutputJsonConverter))]
    public string? PlannerCopilot { get; set; }
}

public sealed class BackendCaseDocumentsResponse
{
    [JsonPropertyName("caseId")]
    public string CaseId { get; set; } = string.Empty;

    [JsonPropertyName("documents")]
    public IReadOnlyList<BackendCaseDocumentResponse> Documents { get; set; } = [];
}

public sealed class BackendCaseDocumentResponse
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = string.Empty;

    [JsonPropertyName("documentPath")]
    public string DocumentPath { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;

    [JsonPropertyName("lastModifiedUtc")]
    public DateTimeOffset LastModifiedUtc { get; set; }
}

public sealed class BackendCaseListResponse
{
    [JsonPropertyName("cases")]
    public IReadOnlyList<BackendCaseSummaryResponse> Cases { get; set; } = [];
}

public sealed class BackendCaseSummaryResponse
{
    [JsonPropertyName("caseId")]
    public string CaseId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("outcomeTag")]
    public string OutcomeTag { get; set; } = string.Empty;

    [JsonPropertyName("legacyId")]
    public string? LegacyId { get; set; }

    [JsonPropertyName("expectedOutcome")]
    public string ExpectedOutcome { get; set; } = string.Empty;

    [JsonPropertyName("context")]
    public BackendCaseContextResponse Context { get; set; } = new();
}

public sealed class BackendCaseContextResponse
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("campaign")]
    public string Campaign { get; set; } = string.Empty;

    [JsonPropertyName("planningHorizon")]
    public string PlanningHorizon { get; set; } = string.Empty;

    [JsonPropertyName("budgetCap")]
    public decimal BudgetCap { get; set; }

    [JsonPropertyName("targetFillRate")]
    public decimal TargetFillRate { get; set; }

    [JsonPropertyName("affectedSkuCount")]
    public int AffectedSkuCount { get; set; }

    [JsonPropertyName("signalSources")]
    public IReadOnlyList<string> SignalSources { get; set; } = [];
}

public sealed class BackendProblemDetailsResponse
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("detail")]
    public string Detail { get; set; } = string.Empty;
}
