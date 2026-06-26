using System.Text.Json;
using CohereInventoryAndTrend.Mcp.Models;

namespace CohereInventoryAndTrend.Mcp.Builders;

public sealed class ReplenishmentPlanBuilder
{
    public BuildReplenishmentDraftResponse Build(string caseId, string executionId)
    {
        var missingFields = new List<string>();
        var blockingIssues = new List<string>();

        if (caseId.Length == 0)
        {
            missingFields.Add("caseId");
        }

        if (executionId.Length == 0)
        {
            missingFields.Add("executionId");
        }

        var planStatus = missingFields.Count == 0
            ? "Draft PO/TO Recommendations Ready"
            : "Additional Information Required";

        var draft = new ReplenishmentPlanDraft
        {
            CaseId = caseId,
            ExecutionId = executionId,
            PlanStatus = planStatus,
            SignalIngestionSummary = JsonDocument.Parse("{}").RootElement,
            FeatureCausalitySummary = JsonDocument.Parse("{}").RootElement,
            ForecastingSummary = JsonDocument.Parse("{}").RootElement,
            OperationalRequirements =
            [
                "Confirm final PO/TO identifiers and supplier confirmations.",
                "Release replenishment recommendations to ERP when approved by the planning workflow."
            ]
        };

        return new BuildReplenishmentDraftResponse
        {
            Draft = draft,
            MissingFields = missingFields,
            BlockingIssues = blockingIssues
        };
    }
}
