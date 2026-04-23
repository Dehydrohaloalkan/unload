namespace Unload.TaskFlow;

/// <summary>
/// Обработчик автоматических переходов после завершения конкретной workflow-задачи.
/// </summary>
public interface IWorkflowTaskTransitionHandler
{
    /// <summary>
    /// Код исходной задачи, после завершения которой должен сработать обработчик.
    /// </summary>
    string SourceTaskCode { get; }

    /// <summary>
    /// Выполняет автоматический переход после завершения задачи.
    /// </summary>
    Task HandleCompletedAsync(WorkflowTaskCompletionContext context, CancellationToken cancellationToken);
}

