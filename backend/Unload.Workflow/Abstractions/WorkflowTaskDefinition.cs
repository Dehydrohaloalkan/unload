namespace Unload.Workflow;

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

