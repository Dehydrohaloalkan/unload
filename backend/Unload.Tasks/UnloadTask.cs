namespace Unload.Tasks;

/// <summary>
/// Абстрактный базовый класс задачи выгрузки.
/// Задача декларирует ограничения запуска; воркфлоу их исполняет.
/// </summary>
public abstract class UnloadTask
{
    /// <summary>Уникальный код задачи.</summary>
    public abstract string Code { get; }

    /// <summary>
    /// Коды задач, которые должны быть успешно завершены сегодня до запуска этой задачи.
    /// Воркфлоу проверяет через <see cref="Unload.Store.TaskExecutionHistoryStore"/>.
    /// </summary>
    public virtual IReadOnlyCollection<string> RequiresCompleted => [];

    /// <summary>Коды задач, с которыми данная задача не может выполняться одновременно.</summary>
    public virtual IReadOnlyCollection<string> ConflictsWith => [];

    /// <summary>
    /// Должно ли дневное окно быть открыто для запуска задачи.
    /// <c>true</c> — для main-выгрузки и extra; <c>false</c> — для preset и probe.
    /// </summary>
    public virtual bool RequiresDailyWindowOpen => false;

    /// <summary>
    /// Должно ли перед запуском выполняться особое preset-окно
    /// (probe пройден + время в пределах окна, см. <see cref="DailyWindowPolicy.CanRunPreset"/>).
    /// <c>true</c> только для preset-задачи.
    /// </summary>
    public virtual bool RequiresPresetWindow => false;

    /// <summary>
    /// Задача deferred: <see cref="ExecuteAsync"/> только стартует фоновую обработку и сразу
    /// возвращает Accepted (main-выгрузка). Синхронные задачи (preset/extra/probe) — <c>false</c>.
    /// Воркфлоу по этому признаку решает, занимать ли foreground-слот.
    /// </summary>
    public virtual bool IsDeferred => false;

    /// <summary>
    /// Выполняет задачу. Для синхронных задач (preset/extra/probe) завершается полностью;
    /// для deferred (main-выгрузка) стартует фоновую обработку и сразу возвращает Accepted.
    /// </summary>
    public abstract Task<TaskExecutionResult> ExecuteAsync(
        TaskLaunchRequest request,
        CancellationToken cancellationToken);
}
