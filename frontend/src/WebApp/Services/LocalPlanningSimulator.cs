using Cohere.InventoryAndTrend.WebApp.Contracts;
using Cohere.InventoryAndTrend.WebApp.Models;

namespace Cohere.InventoryAndTrend.WebApp.Services;

public sealed class LocalPlanningSimulator
{
    private readonly AgentOutputParser _parser;
    private readonly Dictionary<string, SimulationRun> _runs = new(StringComparer.OrdinalIgnoreCase);

    public LocalPlanningSimulator(AgentOutputParser parser)
    {
        _parser = parser;
    }

    public SimulationRun Start(string planId, SeedPlanDefinition scenario, Func<WorkflowStageKey, string> loadStageJson)
    {
        var executionId = Guid.NewGuid().ToString("N");
        var run = new SimulationRun
        {
            PlanId = planId,
            ExecutionId = executionId,
            Scenario = scenario,
            LoadStageJson = loadStageJson,
            Status = WorkflowRunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            PollCount = 0
        };

        _runs[executionId] = run;
        return run;
    }

    public SimulationRun? Get(string executionId) =>
        _runs.TryGetValue(executionId, out var run) ? run : null;

    public WorkflowProgressResponse AdvanceAndBuildStatus(string executionId)
    {
        if (!_runs.TryGetValue(executionId, out var run))
        {
            throw new KeyNotFoundException($"Execution '{executionId}' not found.");
        }

        run.PollCount++;

        // Advance one stage on each poll tick (every 2s by default)
        if (run.Status == WorkflowRunStatus.Running)
        {
            var targetCompletedCount = Math.Min(WorkflowStageUi.OrderedStages.Count, run.PollCount);
            while (run.CompletedStages.Count < targetCompletedCount)
            {
                CompleteNextStage(run);
            }

            if (run.CompletedStages.Count >= WorkflowStageUi.OrderedStages.Count)
            {
                run.Status = WorkflowRunStatus.AwaitingHumanApproval;
                run.CurrentStage = WorkflowStageKey.PlannerCopilot;
            }
            else
            {
                run.CurrentStage = WorkflowStageUi.OrderedStages[run.CompletedStages.Count];
            }
        }

        return BuildProgress(run);
    }

    public WorkflowProgressResponse CompleteWithDecision(string executionId, HumanDecisionRecord decision)
    {
        if (!_runs.TryGetValue(executionId, out var run))
        {
            throw new KeyNotFoundException($"Execution '{executionId}' not found.");
        }

        run.HumanDecision = decision;
        run.Status = decision.Decision == HumanDecisionType.Reject
            ? WorkflowRunStatus.Failed
            : WorkflowRunStatus.Completed;

        return BuildProgress(run);
    }

    private void CompleteNextStage(SimulationRun run)
    {
        var index = run.CompletedStages.Count;
        if (index >= WorkflowStageUi.OrderedStages.Count)
        {
            return;
        }

        var stageKey = WorkflowStageUi.OrderedStages[index];
        var json = run.LoadStageJson(stageKey);
        var output = _parser.Parse(stageKey, json);

        run.CompletedStages[stageKey] = new CompletedStage
        {
            Output = output,
            CompletedAt = DateTimeOffset.UtcNow
        };
    }

    private WorkflowProgressResponse BuildProgress(SimulationRun run)
    {
        var stages = WorkflowStageUi.OrderedStages.Select(stageKey =>
        {
            var isCompleted = run.CompletedStages.TryGetValue(stageKey, out var completed);
            var isCurrent = run.CurrentStage == stageKey && run.Status == WorkflowRunStatus.Running;

            return new WorkflowStageProgress
            {
                StageKey = stageKey,
                Title = WorkflowStageUi.ToTitle(stageKey),
                Status = isCompleted ? "Completed" : isCurrent ? "Running" : "Pending",
                CompletedAt = isCompleted ? completed!.CompletedAt : null,
                Output = isCompleted ? completed!.Output : null
            };
        }).ToList();

        var message = run.Status switch
        {
            WorkflowRunStatus.Running => $"Running {WorkflowStageUi.ToTitle(run.CurrentStage ?? WorkflowStageKey.SignalIngestion)}…",
            WorkflowRunStatus.AwaitingHumanApproval => "Planner review complete — awaiting human approval.",
            WorkflowRunStatus.Completed => "Planning run approved and completed.",
            WorkflowRunStatus.Failed => "Planning run rejected.",
            _ => "Workflow pending."
        };

        return new WorkflowProgressResponse
        {
            PlanId = run.PlanId,
            ExecutionId = run.ExecutionId,
            Status = run.Status,
            CurrentStage = run.CurrentStage,
            StatusMessage = message,
            Stages = stages,
            HumanDecision = run.HumanDecision
        };
    }

    public sealed class SimulationRun
    {
        public required string PlanId { get; init; }

        public required string ExecutionId { get; init; }

        public required SeedPlanDefinition Scenario { get; init; }

        public required Func<WorkflowStageKey, string> LoadStageJson { get; init; }

        public WorkflowRunStatus Status { get; set; }

        public WorkflowStageKey? CurrentStage { get; set; }

        public DateTimeOffset StartedAt { get; init; }

        public int PollCount { get; set; }

        public Dictionary<WorkflowStageKey, CompletedStage> CompletedStages { get; } = new();

        public HumanDecisionRecord? HumanDecision { get; set; }
    }

    public sealed class CompletedStage
    {
        public required AgentStageResult Output { get; init; }

        public DateTimeOffset CompletedAt { get; init; }
    }
}
