namespace Unload.Workflow;

/// <summary>
/// Диспетчер задач workflow, выполняющий registered definitions через реестр.
/// </summary>
public sealed class WorkflowTaskDispatcher : IWorkflowTaskDispatcher
{
    private readonly IWorkflowTaskRegistry _registry;

    public WorkflowTaskDispatcher(IWorkflowTaskRegistry registry)
    {
        _registry = registry;
    }

    public async Task<TResult> DispatchAsync<TRequest, TResult>(
        string taskCode,
        TRequest request,
        CancellationToken cancellationToken)
    {
        var definition = _registry.GetRequired(taskCode);
        var result = await definition.ExecuteAsync(request!, cancellationToken);
        if (result is TResult typedResult)
        {
            return typedResult;
        }

        throw new InvalidOperationException(
            $"Workflow task '{taskCode}' returned '{result?.GetType().Name ?? "null"}', expected '{typeof(TResult).Name}'.");
    }
}
