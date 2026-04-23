namespace Unload.TaskFlow;

/// <summary>
/// Контракт сервиса автоматических переходов между workflow-задачами.
/// </summary>
public interface IWorkflowTaskTransitionService
{
    /// <summary>
    /// Обрабатывает completion hooks для завершившейся задачи.
    /// </summary>
    Task HandleCompletedAsync(string taskCode, object? payload, CancellationToken cancellationToken);
}
