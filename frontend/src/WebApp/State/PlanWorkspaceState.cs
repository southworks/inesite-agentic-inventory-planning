using GrokInventoryAndTrend.WebApp.Configuration;
using GrokInventoryAndTrend.WebApp.Contracts;
using GrokInventoryAndTrend.WebApp.Services;
using Microsoft.Extensions.Options;

namespace GrokInventoryAndTrend.WebApp.State;

public sealed class PlanWorkspaceState : IAsyncDisposable
{
    private readonly IPlanningApiClient _client;
    private readonly WorkflowPollingOptions _pollingOptions;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private Func<Task>? _renderAsync;
    private string? _viewExecutionId;

    public PlanWorkspaceState(IPlanningApiClient client, IOptions<WorkflowPollingOptions> pollingOptions)
    {
        _client = client;
        _pollingOptions = pollingOptions.Value;
    }

    public PlanDetailResponse? CurrentPlan { get; private set; }

    public WorkflowProgressResponse? WorkflowProgress { get; private set; }

    public bool IsBusy { get; private set; }

    public bool IsPollingWorkflow { get; private set; }

    public string? PollingStatusMessage { get; private set; }

    public string? PollError { get; private set; }

    public string? Error { get; private set; }

    public bool CanStartWorkflow =>
        CurrentPlan?.AllowedActions.Contains("StartWorkflow") == true && !IsBusy;

    public bool CanSubmitDecision =>
        WorkflowProgress?.Status == WorkflowRunStatus.AwaitingHumanApproval && !IsBusy;

    public event Action? OnChange;

    public void RegisterRenderAsync(Func<Task> renderAsync) => _renderAsync = renderAsync;

    public void UnregisterRenderAsync(Func<Task> renderAsync)
    {
        if (ReferenceEquals(_renderAsync, renderAsync))
        {
            _renderAsync = null;
        }
    }

    public bool IsPollingExecution(string? executionId) =>
        IsPollingWorkflow
        && WorkflowProgress is not null
        && !string.IsNullOrWhiteSpace(executionId)
        && string.Equals(WorkflowProgress.ExecutionId, executionId, StringComparison.OrdinalIgnoreCase);

    public async Task LoadAsync(string planId, string? executionId, CancellationToken cancellationToken = default)
    {
        if (!ShouldKeepPolling(planId, executionId))
        {
            await StopPollingAsync();
        }

        IsBusy = true;
        Error = null;
        PollError = null;
        await NotifyUiAsync();

        try
        {
            CurrentPlan = await _client.GetPlanAsync(planId, executionId, cancellationToken)
                          ?? throw new InvalidOperationException($"Plan '{planId}' was not found.");

            var effectiveExecutionId = executionId ?? CurrentPlan.ExecutionId;
            _viewExecutionId = effectiveExecutionId;

            if (!string.IsNullOrWhiteSpace(effectiveExecutionId))
            {
                WorkflowProgress = await _client.GetWorkflowStatusAsync(
                    effectiveExecutionId,
                    planId,
                    cancellationToken);
                await RefreshCurrentPlanAsync(cancellationToken);

                if (WorkflowProgress.Status is WorkflowRunStatus.Running or WorkflowRunStatus.AwaitingHumanApproval)
                {
                    await StartPollingAsync(effectiveExecutionId);
                }
            }
            else
            {
                WorkflowProgress = null;
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
            await NotifyUiAsync();
        }
    }

    public async Task StartWorkflowAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentPlan is null || !CanStartWorkflow)
        {
            return;
        }

        IsBusy = true;
        Error = null;
        PollError = null;
        await NotifyUiAsync();

        try
        {
            var start = await _client.StartWorkflowAsync(CurrentPlan.PlanId, cancellationToken);
            _viewExecutionId = start.ExecutionId;
            WorkflowProgress = await _client.GetWorkflowStatusAsync(
                start.ExecutionId,
                CurrentPlan.PlanId,
                cancellationToken);
            await RefreshCurrentPlanAsync(cancellationToken);
            await StartPollingAsync(start.ExecutionId);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
            await NotifyUiAsync();
        }
    }

    public async Task SubmitDecisionAsync(HumanDecisionType decision, string notes, CancellationToken cancellationToken = default)
    {
        if (CurrentPlan?.ExecutionId is null || !CanSubmitDecision)
        {
            return;
        }

        IsBusy = true;
        Error = null;
        await NotifyUiAsync();

        try
        {
            WorkflowProgress = await _client.SubmitHumanDecisionAsync(
                CurrentPlan.PlanId,
                CurrentPlan.ExecutionId,
                new SubmitHumanDecisionRequest { Decision = decision, Notes = notes },
                cancellationToken);

            await RefreshCurrentPlanAsync(cancellationToken);
            await StopPollingAsync();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
            await NotifyUiAsync();
        }
    }

    private async Task StartPollingAsync(string executionId)
    {
        if (IsPollingExecution(executionId))
        {
            return;
        }

        await StopPollingAsync();

        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;

        IsPollingWorkflow = true;
        PollingStatusMessage = "Refreshing workflow progress…";
        PollError = null;
        await NotifyUiAsync();

        _pollTask = PollWorkflowAsync(executionId, token);
    }

    private async Task PollWorkflowAsync(string executionId, CancellationToken token)
    {
        var started = DateTimeOffset.UtcNow;
        var maxDuration = TimeSpan.FromMinutes(_pollingOptions.MaxDurationMinutes);

        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var previousStatus = CurrentPlan?.Status;
                    WorkflowProgress = await _client.GetWorkflowStatusAsync(
                        executionId,
                        CurrentPlan?.PlanId,
                        token);
                    if (CurrentPlan is not null && previousStatus != WorkflowProgress.Status)
                    {
                        await RefreshCurrentPlanAsync(token);
                    }

                    PollError = null;
                    PollingStatusMessage = WorkflowProgress.StatusMessage;
                    await NotifyUiAsync();

                    if (WorkflowProgress.Status is WorkflowRunStatus.Completed
                        or WorkflowRunStatus.Failed
                        or WorkflowRunStatus.AwaitingHumanApproval)
                    {
                        break;
                    }

                    if (DateTimeOffset.UtcNow - started > maxDuration)
                    {
                        Error = "Workflow polling timed out.";
                        break;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    PollError = ex.Message;
                    PollingStatusMessage = $"Refresh failed: {ex.Message}. Retrying…";
                    await NotifyUiAsync();
                }

                await Task.Delay(TimeSpan.FromSeconds(_pollingOptions.IntervalSeconds), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Expected when polling is stopped.
        }
        finally
        {
            IsPollingWorkflow = false;
            PollingStatusMessage = null;
            await NotifyUiAsync();
        }
    }

    private async Task RefreshCurrentPlanAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentPlan is null)
        {
            return;
        }

        var refreshed = await _client.GetPlanAsync(CurrentPlan.PlanId, _viewExecutionId, cancellationToken);
        if (refreshed is not null)
        {
            CurrentPlan = refreshed;
        }
    }

    public async Task StopPollingAsync()
    {
        if (_pollCts is null)
        {
            return;
        }

        await _pollCts.CancelAsync();
        if (_pollTask is not null)
        {
            try
            {
                await _pollTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelling the poll loop.
            }
        }

        _pollCts.Dispose();
        _pollCts = null;
        _pollTask = null;
        IsPollingWorkflow = false;
        PollingStatusMessage = null;
    }

    public async Task Reset()
    {
        await StopPollingAsync();
        CurrentPlan = null;
        WorkflowProgress = null;
        _viewExecutionId = null;
        Error = null;
        PollError = null;
        IsBusy = false;
        await NotifyUiAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopPollingAsync();
        _renderAsync = null;
    }

    private bool ShouldKeepPolling(string planId, string? executionId)
    {
        if (!IsPollingWorkflow || WorkflowProgress is null || CurrentPlan is null)
        {
            return false;
        }

        if (!string.Equals(CurrentPlan.PlanId, planId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(executionId))
        {
            return false;
        }

        return string.Equals(
            WorkflowProgress.ExecutionId,
            executionId,
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task NotifyUiAsync()
    {
        OnChange?.Invoke();
        if (_renderAsync is not null)
        {
            await _renderAsync();
        }
    }
}
