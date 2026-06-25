using System.Text.Json;
using InventoryPlanning.Api.Host.Workflow;
using Microsoft.Extensions.AI;

namespace InventoryPlanning.Api.Host.Services;

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
                sourcePath = document.BlobName,
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

    public static ChatMessage CreateResponsibleAiReviewMessage(
        string caseId,
        string executionId,
        AgentStepResult underwritingResult,
        bool approved,
        string? reviewerComment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentNullException.ThrowIfNull(underwritingResult);

        var payload = new
        {
            caseId,
            executionId,
            underwritingResult = BuildPlanningReviewResultObject(underwritingResult),
            humanDecision = new
            {
                approved,
                reviewerComment = reviewerComment ?? string.Empty
            }
        };

        return CreateJsonMessage(payload);
    }

    private static object BuildTransitionPayload(
        string caseId,
        string executionId,
        AgentStepResult previousResult)
    {
        if (previousResult.RiskLevel is null
            && previousResult.PolicyRefs is null
            && previousResult.Anomalies is null
            && previousResult.KeyFacts is null)
        {
            return new
            {
                caseId,
                executionId,
                summary = previousResult.Summary,
                decision = previousResult.Decision,
                evidence = previousResult.Evidence
            };
        }

        return new
        {
            caseId,
            executionId,
            summary = previousResult.Summary,
            decision = previousResult.Decision,
            evidence = previousResult.Evidence,
            riskLevel = previousResult.RiskLevel,
            policyRefs = previousResult.PolicyRefs,
            anomalies = previousResult.Anomalies,
            keyFacts = previousResult.KeyFacts
        };
    }

    private static object BuildPlanningReviewResultObject(AgentStepResult underwritingResult) =>
        new
        {
            summary = underwritingResult.Summary,
            decision = underwritingResult.Decision,
            evidence = underwritingResult.Evidence,
            riskLevel = underwritingResult.RiskLevel ?? string.Empty,
            policyRefs = underwritingResult.PolicyRefs ?? Array.Empty<string>(),
            anomalies = underwritingResult.Anomalies ?? Array.Empty<string>(),
            keyFacts = underwritingResult.KeyFacts ?? Array.Empty<string>()
        };

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
