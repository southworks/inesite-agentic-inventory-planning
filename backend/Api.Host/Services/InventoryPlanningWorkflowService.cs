using InventoryPlanning.Api.Host.Contracts;
using InventoryPlanning.Api.Host.Workflow;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using AgentWorkflow = Microsoft.Agents.AI.Workflows.Workflow;

namespace InventoryPlanning.Api.Host.Services;

public sealed class InventoryPlanningWorkflowService
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);

    private const string SignalIngestionKey = "SignalIngestion";
    private const string FeatureCausalityKey = "FeatureCausality";
    private const string ForecastingKey = "Forecasting";
    private const string ReplenishmentAllocationKey = "ReplenishmentAllocation";
    private const string PlannerCopilotKey = "PlannerCopilot";

    private readonly FoundryAgentProvider _agentProvider;
    private readonly InventoryPlanningBasicWorkflowFactory _workflowFactory;
    private readonly InMemoryBasicWorkflowStore _store;
    private readonly LocalDocumentStorageService _documentStorage;
    private readonly DocumentTextExtractionService _documentTextExtractionService;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<InventoryPlanningWorkflowService> _logger;

    public InventoryPlanningWorkflowService(
        FoundryAgentProvider agentProvider,
        InventoryPlanningBasicWorkflowFactory workflowFactory,
        InMemoryBasicWorkflowStore store,
        LocalDocumentStorageService documentStorage,
        DocumentTextExtractionService documentTextExtractionService,
        IHostApplicationLifetime applicationLifetime,
        ILogger<InventoryPlanningWorkflowService> logger)
    {
        _agentProvider = agentProvider;
        _workflowFactory = workflowFactory;
        _store = store;
        _documentStorage = documentStorage;
        _documentTextExtractionService = documentTextExtractionService;
        _applicationLifetime = applicationLifetime;
        _logger = logger;
    }

    public async Task<BasicWorkflowStatusResponse> StartBasicWorkflowAsync(
        string caseId,
        string executionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(caseId))
        {
            throw new InvalidOperationException("CaseId is required.");
        }

        if (string.IsNullOrWhiteSpace(executionId))
        {
            throw new InvalidOperationException("ExecutionId is required.");
        }

        IReadOnlyList<LoadedCaseDocument> documents =
            await _documentStorage.LoadCaseDocumentsAsync(caseId, cancellationToken).ConfigureAwait(false);

        if (documents.Count == 0)
        {
            throw new KeyNotFoundException(
                $"Case '{caseId}' was not found in dataset-seed or has no ingest documents under '{_documentStorage.GetCaseDirectoryPath(caseId)}'. Supported cases: case-01 through case-05.");
        }

        IReadOnlyList<NormalizedCaseDocument> normalizedDocuments = await _documentTextExtractionService
            .ExtractAsync(documents, cancellationToken)
            .ConfigureAwait(false);

        var execution = new BasicWorkflowExecution
        {
            ExecutionId = executionId,
            CaseId = caseId.Trim(),
            Status = BasicWorkflowStatus.Running,
            WorkflowCheckpointManager = CheckpointManager.CreateInMemory()
        };
        _store.Save(execution);

        List<ChatMessage> input = CaseWorkflowPayloadBuilder.CreateInitialMessages(
            caseId,
            executionId,
            normalizedDocuments);
        RunInBackground(executionId, input);

        return ToResponse(execution);
    }

    public BasicWorkflowStatusResponse GetBasicWorkflowStatus(string executionId) =>
        ToResponse(_store.GetRequired(executionId));

    private void RunInBackground(string executionId, IList<ChatMessage> input)
    {
        CancellationToken stopping = _applicationLifetime.ApplicationStopping;

        _ = Task.Run(async () =>
        {
            BasicWorkflowExecution execution = _store.GetRequired(executionId);

            try
            {
                FoundryAgents agents = await _agentProvider.GetAgentsAsync(stopping).ConfigureAwait(false);
                AgentWorkflow workflow = _workflowFactory.CreateWorkflow(agents, execution.CaseId, executionId);

                await using StreamingRun run = await InProcessExecution
                    .RunStreamingAsync(
                        workflow,
                        input,
                        execution.WorkflowCheckpointManager ?? throw new InvalidOperationException("Checkpoint manager was not initialized."),
                        executionId,
                        stopping)
                    .ConfigureAwait(false);

                await BasicRunUntilDoneAsync(execution, run, stopping, sendInitialTurnToken: true).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                MarkFailed(execution, "Workflow cancelled because the application is stopping.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Basic workflow failed for execution {ExecutionId}.", executionId);
                MarkFailed(execution, ex.Message);
            }
        }, CancellationToken.None);
    }

    private async Task BasicRunUntilDoneAsync(
        BasicWorkflowExecution execution,
        StreamingRun run,
        CancellationToken cancellationToken,
        bool sendInitialTurnToken)
    {
        using CancellationTokenSource timeoutCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeoutCts.CancelAfter(RunTimeout);

        if (sendInitialTurnToken)
        {
            await SendTurnTokenAsync(run, timeoutCts.Token).ConfigureAwait(false);
        }

        while (!timeoutCts.IsCancellationRequested)
        {
            using CancellationTokenSource idleCts =
                CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);

            idleCts.CancelAfter(IdleTimeout);

            try
            {
                await foreach (WorkflowEvent workflowEvent in run
                    .WatchStreamAsync(idleCts.Token)
                    .ConfigureAwait(false))
                {
                    HandleEventBasic(execution, workflowEvent);

                    if (execution.Status is BasicWorkflowStatus.Completed or BasicWorkflowStatus.Failed)
                    {
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
                when (idleCts.IsCancellationRequested && !timeoutCts.IsCancellationRequested)
            {
                // Idle timeout; query run status and continue driving the workflow.
            }

            RunStatus status = await run.GetStatusAsync(timeoutCts.Token).ConfigureAwait(false);

            switch (status)
            {
                case RunStatus.Ended:
                    execution.Status = BasicWorkflowStatus.Completed;
                    execution.CurrentAgent = null;
                    Touch(execution);
                    return;

                case RunStatus.PendingRequests:
                    await SendTurnTokenAsync(run, timeoutCts.Token).ConfigureAwait(false);
                    break;

                case RunStatus.Running:
                case RunStatus.Idle:
                    break;

                default:
                    return;
            }
        }
    }

    private void HandleEventBasic(BasicWorkflowExecution execution, WorkflowEvent workflowEvent)
    {
        _logger.LogInformation("Event {Type}", workflowEvent.GetType().Name);

        switch (workflowEvent)
        {
            case AgentResponseUpdateEvent updateEvent:
                TryUpdateAgentOutput(
                    execution,
                    updateEvent.ExecutorId,
                    updateEvent.AsResponse(),
                    isFinal: false);
                break;

            case AgentResponseEvent responseEvent:
                TryUpdateAgentOutput(
                    execution,
                    responseEvent.ExecutorId,
                    responseEvent.Response,
                    isFinal: true);
                break;

            case ExecutorInvokedEvent invokedEvent:
                MarkExecutorStarted(execution, invokedEvent.ExecutorId);
                break;

            case ExecutorCompletedEvent completedEvent:
                MarkExecutorCompleted(execution, completedEvent.ExecutorId);
                TryUpdateAgentOutput(
                    execution,
                    completedEvent.ExecutorId,
                    completedEvent.Data,
                    isFinal: true);
                break;

            case ExecutorFailedEvent failedEvent:
                string message = failedEvent.Data?.Message
                    ?? $"Executor '{failedEvent.ExecutorId}' failed.";
                MarkFailed(execution, message);
                break;

            case WorkflowOutputEvent:
                execution.Status = BasicWorkflowStatus.Completed;
                execution.CurrentAgent = null;
                Touch(execution);
                break;

            case SuperStepCompletedEvent superStepCompletedEvent:
                if (
                    !(superStepCompletedEvent.CompletionInfo?.HasPendingMessages ?? false) && 
                    !(superStepCompletedEvent.CompletionInfo?.HasPendingRequests ?? false) && 
                    execution.Agents.TryGetValue(PlannerCopilotKey, out var plannerCopilot) &&
                    plannerCopilot.Status == BasicWorkflowStatus.Completed) {
                    execution.Status = BasicWorkflowStatus.Completed;
                    execution.CurrentAgent = null;
                    Touch(execution);
                }
                break;
        }
    }

    private void MarkExecutorStarted(BasicWorkflowExecution execution, string executorId)
    {
        string? agentKey = MapExecutorToAgentKey(executorId);
        if (agentKey is null)
        {
            return;
        }

        execution.CurrentAgent = agentKey;

        AgentExecutionState state = GetOrCreateAgentState(execution, agentKey);
        state.Status = BasicWorkflowStatus.Running;

        Touch(execution);
    }

    private void MarkExecutorCompleted(BasicWorkflowExecution execution, string executorId)
    {
        string? agentKey = MapExecutorToAgentKey(executorId);
        if (agentKey is null)
        {
            return;
        }

        AgentExecutionState state = GetOrCreateAgentState(execution, agentKey);
        state.Status = BasicWorkflowStatus.Completed;

        if (string.Equals(execution.CurrentAgent, agentKey, StringComparison.OrdinalIgnoreCase))
        {
            execution.CurrentAgent = null;
        }

        Touch(execution);
    }

    private void TryUpdateAgentOutput(
        BasicWorkflowExecution execution,
        string executorId,
        object? data,
        bool isFinal)
    {
        if (data is null)
        {
            return;
        }

        string? rawOutput = data switch
        {
            AgentResponse response => GetAgentResponseText(response),
            ChatMessage[] messages => WorkflowTextExtractor.FromChatMessages(messages),
            IList<ChatMessage> messages => WorkflowTextExtractor.FromChatMessages(messages),
            IEnumerable<ChatMessage> messages => WorkflowTextExtractor.FromChatMessages(messages),
            ChatMessage message => WorkflowTextExtractor.FromChatMessages([message]),
            string text => text,
            _ => null
        };

        if (rawOutput is null)
        {
            _logger.LogDebug(
                "Ignoring workflow payload for executor {ExecutorId} with unsupported data type {PayloadType}.",
                executorId,
                data.GetType().FullName);
            return;
        }

        SaveAgentOutput(execution, executorId, rawOutput, isFinal);
    }

    private void SaveAgentOutput(
        BasicWorkflowExecution execution,
        string executorId,
        string rawOutput,
        bool isFinal)
    {
        string? agentKey = MapExecutorToAgentKey(executorId);
        if (agentKey is null)
        {
            return;
        }

        string normalizedOutput = NormalizeAgentOutput(rawOutput);
        if (string.IsNullOrWhiteSpace(normalizedOutput))
        {
            return;
        }

        AgentExecutionState state = GetOrCreateAgentState(execution, agentKey);
        execution.StreamingBuffers.Remove(agentKey);

        if (!isFinal &&
            execution.AgentOutputs.TryGetValue(agentKey, out string? existingOutput) &&
            normalizedOutput.Length <= existingOutput.Length)
        {
            return;
        }

        execution.AgentOutputs[agentKey] = normalizedOutput;
        state.Output = normalizedOutput;
        Touch(execution);
    }

    private static AgentExecutionState GetOrCreateAgentState(
        BasicWorkflowExecution execution,
        string agentKey)
    {
        if (execution.Agents.TryGetValue(agentKey, out AgentExecutionState? state))
        {
            return state;
        }

        state = new AgentExecutionState
        {
            AgentName = agentKey,
            Status = BasicWorkflowStatus.Pending
        };

        execution.Agents[agentKey] = state;
        return state;
    }

    private static string GetAgentResponseText(AgentResponse response) =>
        WorkflowTextExtractor.GetAgentResponseText(response);

    private static string NormalizeAgentOutput(string rawOutput)
    {
        string trimmed = rawOutput.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        const string assistantPrefix = "[assistant]";
        int assistantIndex = trimmed.LastIndexOf(assistantPrefix, StringComparison.OrdinalIgnoreCase);
        if (assistantIndex >= 0)
        {
            trimmed = trimmed[(assistantIndex + assistantPrefix.Length)..].TrimStart();
        }

        return trimmed;
    }

    private static string? MapExecutorToAgentKey(string executorId)
    {
        string id = executorId.Replace("_", "-");

        if (id.Contains("signal-ingestion", StringComparison.OrdinalIgnoreCase))
        {
            return SignalIngestionKey;
        }

        if (id.Contains("feature-and-causality", StringComparison.OrdinalIgnoreCase))
        {
            return FeatureCausalityKey;
        }

        if (id.Contains("forecasting", StringComparison.OrdinalIgnoreCase))
        {
            return ForecastingKey;
        }

        if (id.Contains("replenishment-and-allocation", StringComparison.OrdinalIgnoreCase))
        {
            return ReplenishmentAllocationKey;
        }

        if (id.Contains("planner-copilot", StringComparison.OrdinalIgnoreCase))
        {
            return PlannerCopilotKey;
        }

        return null;
    }

    private void MarkFailed(BasicWorkflowExecution execution, string reason)
    {
        execution.Status = BasicWorkflowStatus.Failed;
        execution.FailureReason = reason;
        Touch(execution);
    }

    private static void Touch(BasicWorkflowExecution execution)
    {
        execution.LastUpdatedUtc = DateTimeOffset.UtcNow;
    }

    private BasicWorkflowStatusResponse ToResponse(BasicWorkflowExecution execution)
    {
        _store.Save(execution);

        return new BasicWorkflowStatusResponse
        {
            ExecutionId = execution.ExecutionId,
            CaseId = execution.CaseId,
            Status = execution.Status.ToString(),
            AgentOutputs = new BasicWorkflowAgentOutputsResponse
            {
                SignalIngestion = GetOutput(execution, SignalIngestionKey),
                FeatureCausality = GetOutput(execution, FeatureCausalityKey),
                Forecasting = GetOutput(execution, ForecastingKey),
                ReplenishmentAllocation = GetOutput(execution, ReplenishmentAllocationKey),
                PlannerCopilot = GetOutput(execution, PlannerCopilotKey)
            },
            FailureReason = execution.FailureReason,
            LastUpdatedUtc = execution.LastUpdatedUtc
        };
    }

    private static string? GetOutput(BasicWorkflowExecution execution, string key) =>
        execution.AgentOutputs.TryGetValue(key, out string? value) ? value : null;

    private static async Task SendTurnTokenAsync(StreamingRun run, CancellationToken cancellationToken)
    {
        if (!await run.TrySendMessageAsync(new TurnToken(emitEvents: true)).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Failed to send TurnToken to the workflow.");
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
