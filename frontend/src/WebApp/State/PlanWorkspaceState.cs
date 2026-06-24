using Cohere.InventoryAndTrend.WebApp.Configuration;
using Cohere.InventoryAndTrend.WebApp.Contracts;
using Cohere.InventoryAndTrend.WebApp.Services;
using Microsoft.Extensions.Options;

namespace Cohere.InventoryAndTrend.WebApp.State;

public sealed class PlanWorkspaceState : IAsyncDisposable
{
    private readonly IPlanningApiClient _client;
    private readonly WorkflowPollingOptions _pollingOptions;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private Func<Task>? _uiRefresh;

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

    public string? Error { get; private set; }

    public bool CanStartWorkflow =>
        CurrentPlan?.AllowedActions.Contains("StartWorkflow") == true && !IsBusy;

    public bool CanSubmitDecision =>
        WorkflowProgress?.Status == WorkflowRunStatus.AwaitingHumanApproval && !IsBusy;

    public void RegisterUiRefresh(Func<Task> uiRefresh) => _uiRefresh = uiRefresh;

    public async Task LoadAsync(string planId, string? executionId, CancellationToken cancellationToken = default)
    {
        await StopPollingAsync();

        IsBusy = true;
        Error = null;
        await NotifyAsync();

        try
        {
            CurrentPlan = await _client.GetPlanAsync(planId, cancellationToken)
                          ?? throw new InvalidOperationException($"Plan '{planId}' was not found in this session.");

            if (!string.IsNullOrWhiteSpace(executionId))
            {
                WorkflowProgress = await _client.GetWorkflowStatusAsync(executionId, cancellationToken);
                CurrentPlan.Status = WorkflowProgress.Status;
                CurrentPlan.ExecutionId = executionId;

                if (WorkflowProgress.Status is WorkflowRunStatus.Running or WorkflowRunStatus.AwaitingHumanApproval)
                {
                    StartPolling(executionId);
                }
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
            await NotifyAsync();
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
        await NotifyAsync();

        try
        {
            var start = await _client.StartWorkflowAsync(CurrentPlan.PlanId, cancellationToken);
            CurrentPlan.ExecutionId = start.ExecutionId;
            CurrentPlan.Status = start.Status;
            WorkflowProgress = await _client.GetWorkflowStatusAsync(start.ExecutionId, cancellationToken);
            StartPolling(start.ExecutionId);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
            await NotifyAsync();
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
        await NotifyAsync();

        try
        {
            WorkflowProgress = await _client.SubmitHumanDecisionAsync(
                CurrentPlan.PlanId,
                CurrentPlan.ExecutionId,
                new SubmitHumanDecisionRequest { Decision = decision, Notes = notes },
                cancellationToken);

            CurrentPlan.Status = WorkflowProgress.Status;
            await StopPollingAsync();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
            await NotifyAsync();
        }
    }

    private void StartPolling(string executionId)
    {
        _ = StopPollingAsync();

        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;

        IsPollingWorkflow = true;
        PollingStatusMessage = "Refreshing workflow progress…";
        _ = NotifyAsync();

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
                    WorkflowProgress = await _client.GetWorkflowStatusAsync(executionId, token);
                    if (CurrentPlan is not null)
                    {
                        CurrentPlan.Status = WorkflowProgress.Status;
                    }

                    PollingStatusMessage = WorkflowProgress.StatusMessage;

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
                    Error = ex.Message;
                    break;
                }

                await NotifyAsync();
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
            await NotifyAsync();
        }
    }

    private async Task StopPollingAsync()
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

    public async ValueTask DisposeAsync() => await StopPollingAsync();

    private async Task NotifyAsync()
    {
        if (_uiRefresh is not null)
        {
            await _uiRefresh();
        }
    }
}
