using Microsoft.Extensions.Logging;

namespace Unload.TaskFlow;

/// <summary>
/// Сервис автоматических переходов, запускающий completion handlers после завершения задач.
/// </summary>
public class WorkflowTaskTransitionService(
    IEnumerable<IWorkflowTaskTransitionHandler> handlers,
    ILogger<WorkflowTaskTransitionService> logger) : IWorkflowTaskTransitionService
{
    private readonly IReadOnlyList<IWorkflowTaskTransitionHandler> _handlers = handlers.ToArray();
    private readonly ILogger<WorkflowTaskTransitionService> _logger = logger;

    public async Task HandleCompletedAsync(string taskCode, object? payload, CancellationToken cancellationToken)
    {
        var context = new WorkflowTaskCompletionContext(taskCode, payload);
        foreach (var handler in _handlers.Where(x => string.Equals(x.SourceTaskCode, taskCode, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                await handler.HandleCompletedAsync(context, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Workflow transition handler failed. SourceTaskCode: {SourceTaskCode}, Handler: {HandlerType}",
                    taskCode,
                    handler.GetType().Name);
            }
        }
    }
}
