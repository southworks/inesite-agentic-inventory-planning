using InventoryPlanning.Api.Host.Services;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using AgentWorkflow = Microsoft.Agents.AI.Workflows.Workflow;

namespace InventoryPlanning.Api.Host.Workflow;

public static class InventoryPlanningWorkflowConstants
{
    public const string SharedStateScope = "BasicInventoryPlanningWorkflowState";
}

public static class InventoryPlanningAgentNames
{
    public const string SignalIngestion = "signal-ingestion-agent";
    public const string FeatureCausality = "feature-causality-agent";
    public const string Forecasting = "forecasting-agent";
    public const string ReplenishmentAllocation = "replenishment-allocation-agent";
    public const string PlannerCopilot = "planner-copilot-agent";
}

public sealed class InventoryPlanningBasicWorkflowFactory
{
    public AgentWorkflow CreateWorkflow(FoundryAgents agents, string caseId, string executionId)
    {
        var agentHostOptions = new AIAgentHostOptions
        {
            EmitAgentResponseEvents = true,
            ForwardIncomingMessages = false
        };

        var signalIngestion = agents.SignalIngestion.BindAsExecutor(agentHostOptions);
        var featureCausality = agents.FeatureCausality.BindAsExecutor(agentHostOptions);
        var forecasting = agents.Forecasting.BindAsExecutor(agentHostOptions);
        var replenishmentAllocation = agents.ReplenishmentAllocation.BindAsExecutor(agentHostOptions);
        var plannerCopilot = agents.PlannerCopilot.BindAsExecutor(agentHostOptions);

        FunctionExecutor<IList<ChatMessage>> bridge01 = CreatePayloadBridgeExecutor(
            id: "BasicWorkflowBridge01",
            caseId: caseId,
            executionId: executionId,
            sourceAgentName: InventoryPlanningAgentNames.SignalIngestion);
        FunctionExecutor<IList<ChatMessage>> bridge02 = CreatePayloadBridgeExecutor(
            id: "BasicWorkflowBridge02",
            caseId: caseId,
            executionId: executionId,
            sourceAgentName: InventoryPlanningAgentNames.FeatureCausality);
        FunctionExecutor<IList<ChatMessage>> bridge03 = CreatePayloadBridgeExecutor(
            id: "BasicWorkflowBridge03",
            caseId: caseId,
            executionId: executionId,
            sourceAgentName: InventoryPlanningAgentNames.Forecasting);
        FunctionExecutor<IList<ChatMessage>> bridge04 = CreatePayloadBridgeExecutor(
            id: "BasicWorkflowBridge04",
            caseId: caseId,
            executionId: executionId,
            sourceAgentName: InventoryPlanningAgentNames.ReplenishmentAllocation);

        return new WorkflowBuilder(signalIngestion)
            .AddEdge(signalIngestion, bridge01)
            .AddEdge(bridge01, featureCausality)
            .AddEdge(featureCausality, bridge02)
            .AddEdge(bridge02, forecasting)
            .AddEdge(forecasting, bridge03)
            .AddEdge(bridge03, replenishmentAllocation)
            .AddEdge(replenishmentAllocation, bridge04)
            .AddEdge(bridge04, plannerCopilot)
            .WithOutputFrom(plannerCopilot)
            .WithName($"inventory-planning-basic-{executionId}")
            .WithDescription("Inventory planning workflow: Signal Ingestion -> Feature Causality -> Forecasting -> Replenishment Allocation -> Planner Copilot.")
            .Build();
    }

    private static FunctionExecutor<IList<ChatMessage>> CreatePayloadBridgeExecutor(
        string id,
        string caseId,
        string executionId,
        string sourceAgentName)
    {
        return new FunctionExecutor<IList<ChatMessage>>(
            id: id,
            handlerAsync: async (messages, context, cancellationToken) =>
            {
                string rawOutput = WorkflowTextExtractor.FromLastAssistantMessage(messages);
                AgentStepResult result = ParseBridgeOutput(sourceAgentName, rawOutput);
                ChatMessage payload = CaseWorkflowPayloadBuilder.CreateAgentTransitionMessage(
                    caseId,
                    executionId,
                    result);

                await context.SendMessageAsync(payload, cancellationToken: cancellationToken).ConfigureAwait(false);

                await context.SendMessageAsync(new TurnToken(emitEvents: true), cancellationToken: cancellationToken).ConfigureAwait(false);
            },
            sentMessageTypes: [typeof(ChatMessage), typeof(TurnToken)]);
    }

    private static AgentStepResult ParseBridgeOutput(string sourceAgentName, string rawOutput) =>
        AgentStructuredOutputParser.Parse(sourceAgentName, rawOutput);
}
