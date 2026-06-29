using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrokInventoryAndTrend.Api.Workflow;

/// <summary>
/// Structured output contract aligned with agent-provisioning schemas:
/// - Base: agent-structured-output.schema.json
/// - Forecasting: forecasting-structured-output.schema.json
/// - Planner Copilot: planner-copilot-structured-output.schema.json
/// </summary>
public sealed class AgentStructuredOutput
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("decision")]
    public required string Decision { get; init; }

    [JsonPropertyName("evidence")]
    public required string Evidence { get; init; }

    [JsonPropertyName("confidenceLevel")]
    public string? ConfidenceLevel { get; init; }

    [JsonPropertyName("anomalies")]
    public IReadOnlyList<string>? Anomalies { get; init; }

    [JsonPropertyName("keyMetrics")]
    public IReadOnlyList<string>? KeyMetrics { get; init; }

    [JsonPropertyName("approvalAssessment")]
    public string? ApprovalAssessment { get; init; }

    [JsonPropertyName("budgetImpact")]
    public string? BudgetImpact { get; init; }

    [JsonPropertyName("serviceLevelImpact")]
    public string? ServiceLevelImpact { get; init; }

    [JsonPropertyName("concerns")]
    public IReadOnlyList<string>? Concerns { get; init; }

    [JsonPropertyName("recommendations")]
    public IReadOnlyList<string>? Recommendations { get; init; }
}

public sealed class AgentStepResult
{
    public required string AgentName { get; init; }

    public required string Summary { get; init; }

    public required string Decision { get; init; }

    public required string Evidence { get; init; }

    public string? ConfidenceLevel { get; init; }

    public IReadOnlyList<string>? Anomalies { get; init; }

    public IReadOnlyList<string>? KeyMetrics { get; init; }

    public string? ApprovalAssessment { get; init; }

    public string? BudgetImpact { get; init; }

    public string? ServiceLevelImpact { get; init; }

    public IReadOnlyList<string>? Concerns { get; init; }

    public IReadOnlyList<string>? Recommendations { get; init; }

    public required DateTimeOffset CompletedAtUtc { get; init; }

    public static AgentStepResult FromStructuredOutput(string agentName, AgentStructuredOutput output) =>
        new()
        {
            AgentName = agentName,
            Summary = output.Summary,
            Decision = output.Decision,
            Evidence = output.Evidence,
            ConfidenceLevel = output.ConfidenceLevel,
            Anomalies = output.Anomalies,
            KeyMetrics = output.KeyMetrics,
            ApprovalAssessment = output.ApprovalAssessment,
            BudgetImpact = output.BudgetImpact,
            ServiceLevelImpact = output.ServiceLevelImpact,
            Concerns = output.Concerns,
            Recommendations = output.Recommendations,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
}

public static class AgentStructuredOutputParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static AgentStepResult Parse(string agentName, string rawOutput)
    {
        AgentStructuredOutput? structured = TryParseStructuredOutput(rawOutput);
        if (structured is not null)
        {
            return AgentStepResult.FromStructuredOutput(agentName, structured);
        }

        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            throw new InvalidOperationException(
                $"Agent '{agentName}' returned empty output. Expected JSON with summary, decision, and evidence.");
        }

        string trimmedOutput = rawOutput.Trim();
        if (trimmedOutput.Contains("Error (", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Agent '{agentName}' returned an error instead of structured JSON: {Truncate(trimmedOutput)}");
        }

        throw new InvalidOperationException(
            $"Agent '{agentName}' did not return valid structured JSON. Expected properties: summary, decision, evidence.");
    }

    public static AgentStructuredOutput? TryParseStructuredOutput(string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return null;
        }

        string trimmedOutput = rawOutput.Trim();
        if (trimmedOutput.Contains("Error (", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (string candidate in CollectJsonCandidates(rawOutput))
        {
            AgentStructuredOutput? parsed = TryParseStructuredOutputFromJson(candidate);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        return null;
    }

    public static AgentStepResult? TryParse(string agentName, string rawOutput)
    {
        AgentStructuredOutput? structured = TryParseStructuredOutput(rawOutput);
        return structured is null
            ? null
            : AgentStepResult.FromStructuredOutput(agentName, structured);
    }

    private static AgentStructuredOutput? TryParseStructuredOutputFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        AgentStructuredOutput? strict = TryDeserializeStrict(json);
        if (strict is not null &&
            !string.IsNullOrWhiteSpace(strict.Summary) &&
            !string.IsNullOrWhiteSpace(strict.Decision) &&
            !string.IsNullOrWhiteSpace(strict.Evidence))
        {
            return strict;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            JsonElement root = document.RootElement;
            string? summary = ReadRequiredString(root, "summary");
            string? decision = ReadRequiredString(root, "decision");
            string? evidence = ReadEvidence(root, "evidence");

            if (string.IsNullOrWhiteSpace(summary) ||
                string.IsNullOrWhiteSpace(decision) ||
                string.IsNullOrWhiteSpace(evidence))
            {
                return null;
            }

            return new AgentStructuredOutput
            {
                Summary = summary,
                Decision = decision,
                Evidence = evidence,
                ConfidenceLevel = ReadOptionalString(root, "confidenceLevel"),
                Anomalies = ReadOptionalStringArray(root, "anomalies"),
                KeyMetrics = ReadOptionalStringArray(root, "keyMetrics"),
                ApprovalAssessment = ReadOptionalString(root, "approvalAssessment"),
                BudgetImpact = ReadOptionalString(root, "budgetImpact"),
                ServiceLevelImpact = ReadOptionalString(root, "serviceLevelImpact"),
                Concerns = ReadOptionalStringArray(root, "concerns"),
                Recommendations = ReadOptionalStringArray(root, "recommendations")
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AgentStructuredOutput? TryDeserializeStrict(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<AgentStructuredOutput>(text, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadRequiredString(JsonElement root, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.GetRawText();
    }

    private static string? ReadEvidence(JsonElement root, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.ToString()
        };
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.GetRawText();
    }

    private static IReadOnlyList<string>? ReadOptionalStringArray(JsonElement root, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<string>();
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                string? text = item.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    items.Add(text);
                }
            }
        }

        return items.Count == 0 ? null : items;
    }

    private static string NormalizeJsonPayload(string text)
    {
        string normalized = text.Trim();

        if (normalized.StartsWith("```", StringComparison.Ordinal))
        {
            int firstLineBreak = normalized.IndexOf('\n');
            if (firstLineBreak >= 0)
            {
                normalized = normalized[(firstLineBreak + 1)..];
            }

            if (normalized.EndsWith("```", StringComparison.Ordinal))
            {
                normalized = normalized[..^3].TrimEnd();
            }
        }

        return ExtractJsonObject(normalized) ?? normalized.Trim();
    }

    private static IReadOnlyList<string> CollectJsonCandidates(string rawOutput)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddCandidate(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            string trimmed = candidate.Trim();
            if (seen.Add(trimmed))
            {
                candidates.Add(trimmed);
            }
        }

        AddCandidate(NormalizeJsonPayload(rawOutput));
        AddCandidate(ExtractJsonObject(rawOutput));

        string normalized = rawOutput.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        const string jsonFence = "```json\n";
        if (normalized.Contains(jsonFence, StringComparison.OrdinalIgnoreCase))
        {
            foreach (string segment in normalized.Split(
                         jsonFence,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int closingFenceIndex = segment.IndexOf("\n```", StringComparison.Ordinal);
                if (closingFenceIndex > 0)
                {
                    AddCandidate(segment[..closingFenceIndex].Trim());
                }
            }
        }

        foreach (string isolated in ExtractAllJsonObjects(rawOutput))
        {
            AddCandidate(isolated);
        }

        return candidates;
    }

    private static IEnumerable<string> ExtractAllJsonObjects(string text)
    {
        for (int start = 0; start < text.Length; start++)
        {
            if (text[start] != '{')
            {
                continue;
            }

            int depth = 0;
            for (int index = start; index < text.Length; index++)
            {
                char current = text[index];
                if (current == '{')
                {
                    depth++;
                }
                else if (current == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        yield return text[start..(index + 1)];
                        break;
                    }
                }
            }
        }
    }

    private static string? ExtractJsonObject(string text)
    {
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');

        if (start < 0 || end <= start)
        {
            return null;
        }

        return text[start..(end + 1)];
    }

    private static string Truncate(string value)
    {
        const int maxLength = 500;
        return value.Length <= maxLength
            ? value
            : string.Concat(value.AsSpan(0, maxLength), "...");
    }
}
