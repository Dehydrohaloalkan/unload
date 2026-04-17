namespace Unload.TaskFlow;

/// <summary>
/// Контракт сервиса централизованного доступа к пользовательским workflow-задачам.
/// Отвечает за конфликты, порядок выполнения и фиксацию завершенных задач.
/// </summary>
public interface IWorkflowTaskAccessService
{
    /// <summary>
    /// Выполняет foreground-задачу с эксклюзивным доступом.
    /// </summary>
    Task<TResult> ExecuteExclusiveAsync<TResult>(
        string taskCode,
        Func<Task<TResult>> executor,
        bool markCompletedOnSuccess,
        bool adminOverride,
        CancellationToken cancellationToken);

    /// <summary>
    /// Запускает deferred-задачу, которая продолжит выполняться вне текущего запроса.
    /// </summary>
    TResult ExecuteDeferredStart<TResult>(
        string taskCode,
        Func<TResult> starter,
        bool adminOverride);

    /// <summary>
    /// Помечает задачу завершенной успешно.
    /// </summary>
    void MarkCompleted(string taskCode);

    /// <summary>
    /// Сбрасывает in-memory историю завершенных задач.
    /// </summary>
    void ResetCompletedTasks();
}
