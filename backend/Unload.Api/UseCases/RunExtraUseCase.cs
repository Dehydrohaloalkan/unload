using Unload.Api.ErrorHandling;
using Unload.TaskFlow;
using Unload.Workflow;

namespace Unload.Api.UseCases;

public interface IRunExtraUseCase
{
    Task<ScriptTaskRunResult> ExecuteAsync(CancellationToken cancellationToken);
}

public sealed class RunExtraUseCase : IRunExtraUseCase
{
    private readonly IWorkflowTaskDispatcher _dispatcher;
    private readonly ILogger<RunExtraUseCase> _logger;

    public RunExtraUseCase(
        IWorkflowTaskDispatcher dispatcher,
        ILogger<RunExtraUseCase> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task<ScriptTaskRunResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Extra task launch requested.");
            var result = await _dispatcher.DispatchAsync<EmptyWorkflowTaskRequest, ScriptTaskRunResult>(
                WorkflowTaskCodes.Extra,
                new EmptyWorkflowTaskRequest(),
                cancellationToken);
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
