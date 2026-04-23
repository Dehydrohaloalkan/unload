using Unload.Api.ErrorHandling;
using Unload.TaskFlow;
using Unload.Workflow;

namespace Unload.Api.UseCases;

public interface IRunExtraUseCase
{
    Task<ScriptTaskRunResult> ExecuteAsync(bool adminOverride, CancellationToken cancellationToken);
}

public  class RunExtraUseCase : IRunExtraUseCase
{
    private readonly IWorkflowTaskDispatcher _dispatcher;
    private readonly ITaskExecutionHistoryStore _taskExecutionHistoryStore;
    private readonly ILogger<RunExtraUseCase> _logger;

    public RunExtraUseCase(
        IWorkflowTaskDispatcher dispatcher,
        ITaskExecutionHistoryStore taskExecutionHistoryStore,
        ILogger<RunExtraUseCase> logger)
    {
        _dispatcher = dispatcher;
        _taskExecutionHistoryStore = taskExecutionHistoryStore;
        _logger = logger;
    }

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
            throw new ApiProblemException(
                ex.FailureKind == WorkflowTaskFailureKind.Validation
                    ? StatusCodes.Status400BadRequest
                    : StatusCodes.Status409Conflict,
                ex.FailureKind == WorkflowTaskFailureKind.Validation
                    ? "Validation error"
                    : "Extra task conflict",
                ex.Message,
                ex.ErrorCode,
                ex.Extensions);
        }
    }
}
