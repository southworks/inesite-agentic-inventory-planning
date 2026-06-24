using System.Text.Json;
using Cohere.InventoryAndTrend.WebApp.Contracts;
using Cohere.InventoryAndTrend.WebApp.Models;

namespace Cohere.InventoryAndTrend.WebApp.Services;

public sealed class AgentOutputParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AgentStageResult Parse(WorkflowStageKey stageKey, string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new AgentStageResult();
        }

        return stageKey switch
        {
            WorkflowStageKey.SignalIngestion => ParseSignalIngestion(json),
            WorkflowStageKey.FeatureAndCausality => ParseFeatureCausality(json),
            WorkflowStageKey.Forecasting => ParseForecasting(json),
            WorkflowStageKey.ReplenishmentAndAllocation => ParseReplenishment(json),
            WorkflowStageKey.PlannerCopilot => ParsePlannerCopilot(json),
            _ => ParseBase(json)
        };
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
