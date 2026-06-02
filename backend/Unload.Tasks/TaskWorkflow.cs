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
    private readonly RunActivationChannel _runWorkflow;
    private readonly ExtraActivationChannel _extraWorkflow;
    private readonly ILogger<TaskWorkflow> _logger;

    // Потокобезопасный набор активных foreground-задач (preset).
    private readonly object _sync = new();
    private readonly HashSet<string> _activeForegroundTaskCodes = new(StringComparer.OrdinalIgnoreCase);

    public TaskWorkflow(
        IEnumerable<UnloadTask> tasks,
        DailyWindowPolicy window,
        TaskExecutionHistoryStore historyStore,
        RunActivationChannel runWorkflow,
        ExtraActivationChannel extraWorkflow,
        ILogger<TaskWorkflow> logger)
    {
        _tasks = tasks.ToDictionary(static t => t.Code, StringComparer.OrdinalIgnoreCase);
        _window = window;
        _historyStore = historyStore;
        _runWorkflow = runWorkflow;
        _extraWorkflow = extraWorkflow;
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

        // 3. Атомарно под одним локом: проверка конфликтов + захват foreground-слота.
        //    Раздельные «проверить» и «занять» допускали гонку двух параллельных запусков.
        //    Без AdminOverride конфликты енфорсятся; иначе слот просто занимается.
        ClaimSlot(task, enforceConflicts: !request.AdminOverride);

        _logger.LogInformation("Launching task '{TaskCode}'.", task.Code);

        var isDeferred = task.IsDeferred;
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

        // Особое preset-окно: probe пройден + время в пределах окна.
        if (task.RequiresPresetWindow && !_window.CanRunPreset(out var presetReason))
        {
            throw new TaskLaunchException(
                TaskLaunchFailureKind.Conflict,
                "PRESET_GATE_BLOCKED",
                presetReason);
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

        // ConflictsWith — атомарно проверяется вместе с захватом слота в ClaimSlot.

        // Single-active для run: проверяем RunActivationChannel.
        // (run сам deferred и конфликтует с preset/extra; preset тоже сюда попадает через ConflictsWith run.)
        var conflictsWithRun = task.ConflictsWith.Contains(TaskCodes.Run, StringComparer.OrdinalIgnoreCase);
        if (string.Equals(task.Code, TaskCodes.Run, StringComparison.OrdinalIgnoreCase) || conflictsWithRun)
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

        // Single-active для extra и конфликты с активным extra (preset).
        // extra deferred — foreground-слот не занимает, поэтому ClaimSlot его не видит;
        // активность отслеживаем через ExtraActivationChannel.
        var conflictsWithExtra = task.ConflictsWith.Contains(TaskCodes.Extra, StringComparer.OrdinalIgnoreCase);
        if (string.Equals(task.Code, TaskCodes.Extra, StringComparison.OrdinalIgnoreCase) || conflictsWithExtra)
        {
            var activeExtraId = _extraWorkflow.GetActiveCorrelationId();
            if (!string.IsNullOrEmpty(activeExtraId))
            {
                throw new TaskLaunchException(
                    TaskLaunchFailureKind.Conflict,
                    "TASK_ALREADY_RUNNING",
                    "Extra task is already active.",
                    new Dictionary<string, object?> { ["activeCorrelationId"] = activeExtraId });
            }
        }
    }

    /// <summary>
    /// Атомарно (под одним локом) проверяет конфликты с активными foreground-задачами
    /// и занимает foreground-слот. Объединение проверки и захвата исключает гонку,
    /// когда два параллельных запуска оба проходят проверку до того, как кто-то занял слот.
    /// </summary>
    private void ClaimSlot(UnloadTask task, bool enforceConflicts)
    {
        lock (_sync)
        {
            if (enforceConflicts)
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

            // Deferred-задача (run) живёт в RunActivationChannel и foreground-слот не занимает.
            if (!task.IsDeferred)
            {
                _activeForegroundTaskCodes.Add(task.Code);
            }
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
