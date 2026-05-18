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
    /// Выполняет задачу. Для синхронных задач (preset/extra/probe) завершается полностью;
    /// для deferred (main-выгрузка) стартует фоновую обработку и сразу возвращает Accepted.
    /// </summary>
    public abstract Task<TaskExecutionResult> ExecuteAsync(
        TaskLaunchRequest request,
        CancellationToken cancellationToken);
}
