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

/// <summary>
/// Базовый generic-контракт описания задачи workflow.
/// </summary>
/// <typeparam name="TRequest">Тип входного запроса.</typeparam>
/// <typeparam name="TResult">Тип результата.</typeparam>
public abstract class WorkflowTaskDefinition<TRequest, TResult> : IWorkflowTaskDefinition
{
    public abstract string TaskCode { get; }

    public Type RequestType => typeof(TRequest);

    public Type ResultType => typeof(TResult);

    public async Task<object?> ExecuteAsync(object request, CancellationToken cancellationToken)
    {
        if (request is not TRequest typedRequest)
        {
            throw new InvalidOperationException(
                $"Task '{TaskCode}' expected request of type '{typeof(TRequest).Name}', but got '{request.GetType().Name}'.");
        }

        return await ExecuteTypedAsync(typedRequest, cancellationToken);
    }

    /// <summary>
    /// Выполняет задачу с типизированным запросом.
    /// </summary>
    protected abstract Task<TResult> ExecuteTypedAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Контракт реестра задач workflow.
/// </summary>
public interface IWorkflowTaskRegistry
{
    /// <summary>
    /// Возвращает зарегистрированную задачу по коду или бросает исключение.
    /// </summary>
    IWorkflowTaskDefinition GetRequired(string taskCode);
}

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
