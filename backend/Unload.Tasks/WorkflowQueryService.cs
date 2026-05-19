using Unload.Store;

namespace Unload.Tasks;

/// <summary>
/// Агрегированный snapshot воркфлоу для UI-дашборда.
/// </summary>
public sealed record WorkflowDashboard(
    PresetGateState PresetState,
    bool HasRunToday,
    bool HasExtraToday,
    DateTimeOffset? RunLastCompletedAt,
    DateTimeOffset? ExtraLastCompletedAt,
    IReadOnlyCollection<TaskRecord> TodayHistory);

/// <summary>
/// История запусков и завершённых задач за диапазон дней.
/// </summary>
public sealed record WorkflowHistory(
    DateOnly FromDayInclusive,
    DateOnly ToDayInclusive,
    int Days,
    IReadOnlyList<RunStatusInfo> Runs,
    IReadOnlyList<TaskRecord> TaskHistory);

/// <summary>
/// Доменные запросы агрегированных представлений воркфлоу.
/// Выносит агрегацию (фильтры по датам, сборку snapshot'ов) из тонких контроллеров API.
/// </summary>
public sealed class WorkflowQueryService(
    RunStateStore runStateStore,
    TaskExecutionHistoryStore historyStore,
    DailyWindowPolicy dailyWindowPolicy)
{
    /// <summary>Минимально допустимая глубина истории в днях.</summary>
    public const int MinHistoryDays = 1;

    /// <summary>Максимально допустимая глубина истории в днях.</summary>
    public const int MaxHistoryDays = 365;

    private readonly RunStateStore _runStateStore = runStateStore;
    private readonly TaskExecutionHistoryStore _historyStore = historyStore;
    private readonly DailyWindowPolicy _dailyWindowPolicy = dailyWindowPolicy;

    /// <summary>Возвращает запуски <c>run</c> за текущий день.</summary>
    public IReadOnlyList<RunStatusInfo> GetTodayRuns()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return _runStateStore
            .List()
            .Where(run =>
                string.Equals(run.TaskCode, TaskCodes.Run, StringComparison.OrdinalIgnoreCase) &&
                DateOnly.FromDateTime(run.CreatedAt.LocalDateTime) == today)
            .OrderByDescending(static run => run.CreatedAt)
            .ToArray();
    }

    /// <summary>Собирает snapshot дашборда: состояние окна, флаги дня, последние завершения.</summary>
    public WorkflowDashboard GetDashboard()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var history = _historyStore.List(today);

        var runLastCompletedAt = history
            .FirstOrDefault(record => string.Equals(record.TaskCode, TaskCodes.Run, StringComparison.OrdinalIgnoreCase))
            ?.CompletedAt;
        var extraLastCompletedAt = history
            .FirstOrDefault(record => string.Equals(record.TaskCode, TaskCodes.Extra, StringComparison.OrdinalIgnoreCase))
            ?.CompletedAt;

        return new WorkflowDashboard(
            _dailyWindowPolicy.Get(),
            _historyStore.HasRunToday(TaskCodes.Run, today),
            _historyStore.HasRunToday(TaskCodes.Extra, today),
            runLastCompletedAt,
            extraLastCompletedAt,
            history);
    }

    /// <summary>
    /// Возвращает историю run + завершённых задач за последние <paramref name="days"/> дней.
    /// Глубина зажимается в диапазон [<see cref="MinHistoryDays"/>; <see cref="MaxHistoryDays"/>].
    /// </summary>
    public WorkflowHistory GetHistory(int days)
    {
        var effectiveDays = Math.Clamp(days, MinHistoryDays, MaxHistoryDays);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var fromDay = today.AddDays(-(effectiveDays - 1));

        var runs = _runStateStore
            .List()
            .Where(run =>
            {
                var day = DateOnly.FromDateTime(run.CreatedAt.LocalDateTime);
                return day >= fromDay && day <= today;
            })
            .OrderByDescending(static run => run.CreatedAt)
            .ToArray();

        var taskHistory = _historyStore.ListRange(fromDay, today);

        return new WorkflowHistory(fromDay, today, effectiveDays, runs, taskHistory);
    }
}
