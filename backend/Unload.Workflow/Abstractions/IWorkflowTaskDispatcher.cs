namespace Unload.Workflow;

/// <summary>
/// Контракт диспетчера задач workflow.
/// </summary>
public interface IWorkflowTaskDispatcher
{
    /// <summary>
    /// Выполняет задачу по коду через реестр зарегистрированных definitions.
    /// </summary>
    Task<TResult> DispatchAsync<TRequest, TResult>(
        string taskCode,
        TRequest request,
        CancellationToken cancellationToken);
}
