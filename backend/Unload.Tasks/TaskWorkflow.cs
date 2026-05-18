using Microsoft.Extensions.Logging;
using Unload.Core;
using Unload.Store;

namespace Unload.Tasks;

/// <summary>
/// Единственный класс оркестрации задач выгрузки.
/// Контролирует ВСЕ запреты на запуск: дневное окно, зависимости, конфликты, single-active.
/// Без интерфейса — одна реализация.
/// </summary>
public class TaskWorkflow
{
    private readonly IReadOnlyDictionary<string, UnloadTask> _tasks;
    private readonly DailyWindowPolicy _window;
    private readonly TaskExecutionHistoryStore _historyStore;
    private readonly ISingleActiveWorkflow<RunRequest> _runWorkflow;
    private readonly ILogger<TaskWorkflow> _logger;

    // Потокобезопасный набор активных foreground-задач (preset, extra).
    private readonly object _sync = new();
    private readonly HashSet<string> _activeForegroundTaskCodes = new(StringComparer.OrdinalIgnoreCase);

    public TaskWorkflow(
        IEnumerable<UnloadTask> tasks,
        DailyWindowPolicy window,
        TaskExecutionHistoryStore historyStore,
        ISingleActiveWorkflow<RunRequest> runWorkflow,
        ILogger<TaskWorkflow> logger)
    {
        _tasks = tasks.ToDictionary(static t => t.Code, StringComparer.OrdinalIgnoreCase);
        _window = window;
        _historyStore = historyStore;
        _runWorkflow = runWorkflow;
        _logger = logger;
    }

    /// <summary>
    /// Запускает задачу по коду из <see cref="TaskLaunchRequest"/>.
    /// </summary>
    public async Task<TaskExecutionResult> LaunchAsync(TaskLaunchRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Резолв задачи по коду.
        if (!_tasks.TryGetValue(request.TaskCode, out var task))
        {
            throw new TaskLaunchException(
                TaskLaunchFailureKind.Validation,
                "VALIDATION_ERROR",
                $"Task '{request.TaskCode}' is not registered.");
        }

        // 2. Проверки запретов (пропускаются при AdminOverride).
        if (!request.AdminOverride)
        {
            EnsureCanLaunch(task);
        }

        _logger.LogInformation("Launching task '{TaskCode}'.", task.Code);

        // 3. Для foreground-задач отмечаем начало и конец в _activeForegroundTaskCodes.
        var isDeferred = string.Equals(task.Code, TaskCodes.Run, StringComparison.OrdinalIgnoreCase);
        if (!isDeferred)
        {
            BeginForeground(task.Code);
        }

        try
        {
            // 4. Выполнение задачи.
            var result = await task.ExecuteAsync(request, ct);
            _logger.LogInformation(
                "Task '{TaskCode}' finished with status {Status}.",
                task.Code,
                result.Status);
            return result;
        }
        finally
        {
            if (!isDeferred)
            {
                EndForeground(task.Code);
            }
        }
    }

    private void EnsureCanLaunch(UnloadTask task)
    {
        var now = DateTime.Now;

        // Дневное окно.
        if (task.RequiresDailyWindowOpen && !_window.IsOpen(now))
        {
            throw new TaskLaunchException(
                TaskLaunchFailureKind.Conflict,
                "PRESET_GATE_BLOCKED",
                $"Task '{task.Code}' requires the daily window to be open. " +
                "Complete preset first or wait for the window to open.");
        }

        // Специальная проверка для preset: probe пройден + время окна.
        if (string.Equals(task.Code, TaskCodes.Preset, StringComparison.OrdinalIgnoreCase))
        {
            if (!_window.CanRunPreset(out var presetReason))
            {
                throw new TaskLaunchException(
                    TaskLaunchFailureKind.Conflict,
                    "PRESET_GATE_BLOCKED",
                    presetReason);
            }
        }

        var today = DateOnly.FromDateTime(now);

        // RequiresCompleted — каждый код должен иметь успешный TaskExecution за сегодня.
        var missingCodes = task.RequiresCompleted
            .Where(code => !_historyStore.HasRunToday(code, today))
            .ToArray();
        if (missingCodes.Length > 0)
        {
            throw new TaskLaunchException(
                TaskLaunchFailureKind.Conflict,
                "TASK_DEPENDENCY_NOT_SATISFIED",
                $"Task '{task.Code}' requires completed tasks: {string.Join(", ", missingCodes)}.",
                new Dictionary<string, object?> { ["requiredTaskCodes"] = missingCodes });
        }

        // ConflictsWith — ни одна конфликтующая задача не должна быть активна прямо сейчас.
        lock (_sync)
        {
            var conflicting = _activeForegroundTaskCodes
                .FirstOrDefault(activeCode =>
                    task.ConflictsWith.Contains(activeCode, StringComparer.OrdinalIgnoreCase) ||
                    (_tasks.TryGetValue(activeCode, out var activeTask) &&
                     activeTask.ConflictsWith.Contains(task.Code, StringComparer.OrdinalIgnoreCase)));

            if (conflicting is not null)
            {
                throw new TaskLaunchException(
                    TaskLaunchFailureKind.Conflict,
                    "TASK_ALREADY_RUNNING",
                    $"Task '{conflicting}' is already running.");
            }
        }

        // Single-active для run: проверяем ISingleActiveWorkflow<RunRequest>.
        var isRunTask = string.Equals(task.Code, TaskCodes.Run, StringComparison.OrdinalIgnoreCase);
        var conflictsWithRun = task.ConflictsWith.Contains(TaskCodes.Run, StringComparer.OrdinalIgnoreCase);
        if (isRunTask || conflictsWithRun)
        {
            var activeRunId = _runWorkflow.GetActiveCorrelationId();
            if (!string.IsNullOrEmpty(activeRunId))
            {
                throw new TaskLaunchException(
                    TaskLaunchFailureKind.Conflict,
                    "RUN_ALREADY_IN_PROGRESS",
                    "Another run task is already active.",
                    new Dictionary<string, object?> { ["activeCorrelationId"] = activeRunId });
            }
        }
    }

    private void BeginForeground(string taskCode)
    {
        lock (_sync)
        {
            _activeForegroundTaskCodes.Add(taskCode);
        }
    }

    private void EndForeground(string taskCode)
    {
        lock (_sync)
        {
            _activeForegroundTaskCodes.Remove(taskCode);
        }
    }
}
