namespace Unload.Workflow;

/// <summary>
/// Негeneric-контракт описания задачи workflow для регистрации в реестре.
/// </summary>
public interface IWorkflowTaskDefinition
{
    /// <summary>
    /// Код задачи workflow.
    /// </summary>
    string TaskCode { get; }

    /// <summary>
    /// Тип входного запроса задачи.
    /// </summary>
    Type RequestType { get; }

    /// <summary>
    /// Тип результата задачи.
    /// </summary>
    Type ResultType { get; }

    /// <summary>
    /// Выполняет задачу workflow с переданным запросом.
    /// </summary>
    /// <param name="request">Запрос задачи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат выполнения в boxed-форме.</returns>
    Task<object?> ExecuteAsync(object request, CancellationToken cancellationToken);
}

