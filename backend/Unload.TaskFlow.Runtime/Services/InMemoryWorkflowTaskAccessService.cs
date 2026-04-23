using Unload.Run.Application;
using Unload.TaskFlow;

namespace Unload.TaskFlow.Runtime;

/// <summary>
/// In-memory сервис централизованного контроля доступа к workflow-задачам.
/// </summary>
public  class InMemoryWorkflowTaskAccessService : IWorkflowTaskAccessService
{
    private readonly object _sync = new();
    private readonly IWorkflowTaskDependencyCatalog _dependencyCatalog;
    private readonly IWorkflowStageStateStore _workflowStageStateStore;
    private readonly IRunCoordinator _runCoordinator;
    private readonly HashSet<string> _completedTaskCodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeForegroundTaskCodes = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryWorkflowTaskAccessService(
        IWorkflowTaskDependencyCatalog dependencyCatalog,
        IWorkflowStageStateStore workflowStageStateStore,
        IRunCoordinator runCoordinator)
    {
        _dependencyCatalog = dependencyCatalog;
        _workflowStageStateStore = workflowStageStateStore;
        _runCoordinator = runCoordinator;
    }

    public async Task<TResult> ExecuteExclusiveAsync<TResult>(
        string taskCode,
        Func<Task<TResult>> executor,
        bool markCompletedOnSuccess,
        bool adminOverride,
        CancellationToken cancellationToken)
    {
        BeginForegroundTask(taskCode, adminOverride);
        try
        {
            var result = await executor();
            if (markCompletedOnSuccess)
            {
                MarkCompleted(taskCode);
            }

            return result;
        }
        finally
        {
            EndForegroundTask(taskCode);
        }
    }

    public TResult ExecuteDeferredStart<TResult>(
        string taskCode,
        Func<TResult> starter,
        bool adminOverride)
    {
        lock (_sync)
        {
            EnsureCanStartLocked(taskCode, adminOverride);
            return starter();
        }
    }

    public void MarkCompleted(string taskCode)
    {
        lock (_sync)
        {
            _completedTaskCodes.Add(taskCode);
        }
    }

    public void ResetCompletedTasks()
    {
        lock (_sync)
        {
            _completedTaskCodes.Clear();
        }
    }

    private void BeginForegroundTask(string taskCode, bool adminOverride)
    {
        lock (_sync)
        {
            EnsureCanStartLocked(taskCode, adminOverride);
            _activeForegroundTaskCodes.Add(taskCode);
        }
    }

    private void EndForegroundTask(string taskCode)
    {
        lock (_sync)
        {
            _activeForegroundTaskCodes.Remove(taskCode);
        }
    }

    private void EnsureCanStartLocked(string taskCode, bool adminOverride)
    {
        var rule = _dependencyCatalog.GetRequired(taskCode);
        var conflictingForegroundTaskCode = _activeForegroundTaskCodes.FirstOrDefault(activeTaskCode =>
        {
            var activeRule = _dependencyCatalog.GetRequired(activeTaskCode);
            return rule.ConflictsWithTaskCodes.Contains(activeTaskCode, StringComparer.OrdinalIgnoreCase) ||
                   activeRule.ConflictsWithTaskCodes.Contains(taskCode, StringComparer.OrdinalIgnoreCase);
        });
        if (!string.IsNullOrWhiteSpace(conflictingForegroundTaskCode))
        {
            throw new WorkflowTaskDispatchException(
                WorkflowTaskFailureKind.Conflict,
                "TASK_ALREADY_RUNNING",
                $"Task '{conflictingForegroundTaskCode}' is already running.");
        }

        var activeRunCorrelationId = _runCoordinator.GetActiveCorrelationId();
        if (!string.IsNullOrWhiteSpace(activeRunCorrelationId) &&
            (string.Equals(taskCode, WorkflowTaskCodes.Run, StringComparison.OrdinalIgnoreCase) ||
             rule.ConflictsWithTaskCodes.Contains(WorkflowTaskCodes.Run, StringComparer.OrdinalIgnoreCase)))
        {
            throw new WorkflowTaskDispatchException(
                WorkflowTaskFailureKind.Conflict,
                "RUN_ALREADY_IN_PROGRESS",
                "Another run task is already active.",
                new Dictionary<string, object?> { ["activeCorrelationId"] = activeRunCorrelationId });
        }

        if (!adminOverride)
        {
            var missingTaskCodes = rule.RequiresCompletedCodes
                .Where(requiredCode => !_completedTaskCodes.Contains(requiredCode) && !_workflowStageStateStore.IsCompleted(requiredCode))
                .ToArray();

            if (missingTaskCodes.Length > 0)
            {
                throw new WorkflowTaskDispatchException(
                    WorkflowTaskFailureKind.Conflict,
                    "TASK_DEPENDENCY_NOT_SATISFIED",
                    $"Task '{taskCode}' requires completed tasks: {string.Join(", ", missingTaskCodes)}.",
                    new Dictionary<string, object?> { ["requiredTaskCodes"] = missingTaskCodes });
            }
        }
    }
}
