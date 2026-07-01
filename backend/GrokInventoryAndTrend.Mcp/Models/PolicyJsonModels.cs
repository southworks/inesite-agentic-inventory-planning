using System.Text.Json.Serialization;

namespace GrokInventoryAndTrend.Mcp.Models;

public sealed class PolicyJsonDocument
{
    [JsonPropertyName("policies")]
    public List<PolicyJsonEntry> Policies { get; set; } = [];
}

public sealed class PolicyJsonEntry
{
    [JsonPropertyName("policyRef")]
    public string PolicyRef { get; set; } = string.Empty;

    [JsonPropertyName("rule")]
    public string Rule { get; set; } = string.Empty;

    [JsonPropertyName("threshold")]
    public string Threshold { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("exception")]
    public string Exception { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    public PolicyEntry ToPolicyEntry() =>
        new()
        {
            PolicyRef = PolicyRef,
            Rule = Rule,
            Threshold = Threshold,
            Action = Action,
            Exception = Exception,
            FullText = Content
        };
}
