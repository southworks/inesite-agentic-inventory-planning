using System.Text.Json;
using GrokInventoryAndTrend.WebApp.Contracts;
using GrokInventoryAndTrend.WebApp.Models;

namespace GrokInventoryAndTrend.WebApp.Services;

public sealed class AgentOutputParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AgentStageResult Parse(WorkflowStageKey stageKey, string json)
    {
        var normalized = NormalizeAgentOutput(json);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new AgentStageResult();
        }

        if (!LooksLikeJson(normalized))
        {
            return new AgentStageResult
            {
                Summary = normalized,
                Decision = "Output received",
                Evidence = normalized
            };
        }

        try
        {
            return stageKey switch
            {
                WorkflowStageKey.SignalIngestion => ParseSignalIngestion(normalized),
                WorkflowStageKey.FeatureAndCausality => ParseFeatureCausality(normalized),
                WorkflowStageKey.Forecasting => ParseForecasting(normalized),
                WorkflowStageKey.ReplenishmentAndAllocation => ParseReplenishment(normalized),
                WorkflowStageKey.PlannerCopilot => ParsePlannerCopilot(normalized),
                _ => ParseBase(normalized)
            };
        }
        catch (JsonException)
        {
            return new AgentStageResult
            {
                Summary = normalized,
                Decision = "Output received",
                Evidence = normalized
            };
        }
    }

    internal static string NormalizeAgentOutput(string rawOutput)
    {
        var trimmed = rawOutput.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        const string assistantPrefix = "[assistant]";
        var assistantIndex = trimmed.LastIndexOf(assistantPrefix, StringComparison.OrdinalIgnoreCase);
        if (assistantIndex >= 0)
        {
            trimmed = trimmed[(assistantIndex + assistantPrefix.Length)..].TrimStart();
        }

        return trimmed;
    }

    private static bool LooksLikeJson(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private static AgentStageResult ParseBase(string json)
    {
        return JsonSerializer.Deserialize<AgentStageResult>(json, JsonOptions) ?? new AgentStageResult();
    }

    private static SignalIngestionStageResult ParseSignalIngestion(string json)
    {
        var result = JsonSerializer.Deserialize<SignalIngestionStageResult>(json, JsonOptions)
                     ?? new SignalIngestionStageResult();
        result.SourcesIngested ??= [];
        result.QualityFlags ??= [];
        return result;
    }

    private static FeatureCausalityStageResult ParseFeatureCausality(string json)
    {
        var result = JsonSerializer.Deserialize<FeatureCausalityStageResult>(json, JsonOptions)
                     ?? new FeatureCausalityStageResult();
        result.TopDrivers ??= [];
        result.ElasticityNotes ??= [];
        return result;
    }

    private static ForecastingStageResult ParseForecasting(string json)
    {
        var result = JsonSerializer.Deserialize<ForecastingStageResult>(json, JsonOptions)
                     ?? new ForecastingStageResult();
        result.Anomalies ??= [];
        result.KeyMetrics ??= [];
        return result;
    }

    private static ReplenishmentStageResult ParseReplenishment(string json)
    {
        var result = JsonSerializer.Deserialize<ReplenishmentStageResult>(json, JsonOptions)
                     ?? new ReplenishmentStageResult();
        result.LineItems ??= [];
        return result;
    }

    private static PlannerCopilotStageResult ParsePlannerCopilot(string json)
    {
        var result = JsonSerializer.Deserialize<PlannerCopilotStageResult>(json, JsonOptions)
                     ?? new PlannerCopilotStageResult();
        result.Concerns ??= [];
        result.Recommendations ??= [];
        return result;
    }
}
