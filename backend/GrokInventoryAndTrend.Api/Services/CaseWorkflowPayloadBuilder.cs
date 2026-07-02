using System.Text.Json;
using GrokInventoryAndTrend.Api.Workflow;
using Microsoft.Extensions.AI;

namespace GrokInventoryAndTrend.Api.Services;

public static class CaseWorkflowPayloadBuilder
{
    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        WriteIndented = false
    };

    public static List<ChatMessage> CreateInitialMessages(
        string caseId,
        string executionId,
        IReadOnlyList<NormalizedCaseDocument> documents)
    {
        var payload = new
        {
            caseId,
            executionId,
            documents = documents.Select(document => new
            {
                documentId = Path.GetFileNameWithoutExtension(document.FileName),
                fileName = document.FileName,
                sourcePath = document.DocumentPath,
                documentType = document.ContentType,
                extractedText = document.ExtractedText,
                extractionMode = document.ExtractionMode,
                extractionSucceeded = document.ExtractionSucceeded,
                extractionMessage = document.ExtractionMessage
            })
        };

        return CreateJsonMessages(payload);
    }

    public static ChatMessage CreateAgentTransitionMessage(
        string caseId,
        string executionId,
        AgentStepResult previousResult)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentNullException.ThrowIfNull(previousResult);

        return CreateJsonMessage(BuildTransitionPayload(caseId, executionId, previousResult));
    }

    private static object BuildTransitionPayload(
        string caseId,
        string executionId,
        AgentStepResult previousResult)
    {
        return new
        {
            caseId,
            executionId,
            previousAgent = previousResult.AgentName,
            summary = previousResult.Summary,
            decision = previousResult.Decision,
            evidence = previousResult.Evidence,
            confidenceLevel = previousResult.ConfidenceLevel,
            anomalies = previousResult.Anomalies,
            keyMetrics = previousResult.KeyMetrics,
            approvalAssessment = previousResult.ApprovalAssessment,
            budgetImpact = previousResult.BudgetImpact,
            serviceLevelImpact = previousResult.ServiceLevelImpact,
            concerns = previousResult.Concerns,
            recommendations = previousResult.Recommendations,
            completedAtUtc = previousResult.CompletedAtUtc
        };
    }

    private static List<ChatMessage> CreateJsonMessages(object payload)
    {
        return [CreateJsonMessage(payload)];
    }

    private static ChatMessage CreateJsonMessage(object payload)
    {
        string json = JsonSerializer.Serialize(payload, CompactJsonOptions);
        return new ChatMessage(ChatRole.User, json);
    }
}
