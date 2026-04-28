using Unload.Api.ErrorHandling;
using Unload.Api.Abstractions;
using Unload.Api.UseCases.Abstractions;
using Unload.TaskFlow;
using Unload.TaskFlow.Exceptions;
using Unload.Workflow;

namespace Unload.Api.UseCases;

public class RunExtraUseCase(
    IWorkflowTaskDispatcher dispatcher,
    ITaskExecutionHistoryStore taskExecutionHistoryStore,
    ILogger<RunExtraUseCase> logger) : IRunExtraUseCase
{
    private readonly IWorkflowTaskDispatcher _dispatcher = dispatcher;
    private readonly ITaskExecutionHistoryStore _taskExecutionHistoryStore = taskExecutionHistoryStore;
    private readonly ILogger<RunExtraUseCase> _logger = logger;

    public async Task<ScriptTaskRunResult> ExecuteAsync(bool adminOverride, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            _logger.LogInformation("Extra task launch requested.");
            var result = await _dispatcher.DispatchAsync<EmptyWorkflowTaskRequest, ScriptTaskRunResult>(
                WorkflowTaskCodes.Extra,
                new EmptyWorkflowTaskRequest(adminOverride),
                cancellationToken);
            _taskExecutionHistoryStore.Add(
                WorkflowTaskCodes.Extra,
                startedAt,
                DateTimeOffset.UtcNow,
                result.CorrelationId,
                result.Message,
                result.ScriptsExecuted,
                result.FilesWritten,
                result.OutputPath);
            _logger.LogInformation(
                "Extra task completed. CorrelationId: {CorrelationId}, ScriptsExecuted: {ScriptsExecuted}, FilesWritten: {FilesWritten}",
                result.CorrelationId,
                result.ScriptsExecuted,
                result.FilesWritten);
            return result;
        }
        catch (WorkflowTaskDispatchException ex)
        {
            _logger.LogWarning("Extra task rejected. Code: {ErrorCode}, Message: {Message}", ex.ErrorCode, ex.Message);
            throw WorkflowDispatchExceptions.ToApiProblem(ex, "Extra task conflict");
        }
    }
}
