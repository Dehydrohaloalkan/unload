using Unload.Api.ErrorHandling;
using Unload.Api.UseCases.Abstractions;
using Unload.Tasks;

namespace Unload.Api.UseCases;

public class RunExtraUseCase(
    TaskWorkflow taskWorkflow,
    ILogger<RunExtraUseCase> logger) : IRunExtraUseCase
{
    private readonly TaskWorkflow _taskWorkflow = taskWorkflow;
    private readonly ILogger<RunExtraUseCase> _logger = logger;

    public async Task<ScriptTaskRunResult> ExecuteAsync(bool adminOverride, bool publishToGateway, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Extra task launch requested.");
            var result = await _taskWorkflow.LaunchAsync(
                new TaskLaunchRequest(
                    TaskCode: TaskCodes.Extra,
                    AdminOverride: adminOverride,
                    PublishToGateway: publishToGateway),
                cancellationToken);

            _logger.LogInformation(
                "Extra task completed. CorrelationId: {CorrelationId}, ScriptsExecuted: {ScriptsExecuted}, FilesWritten: {FilesWritten}",
                result.ExecutionId,
                result.ScriptsExecuted,
                result.FilesWritten);

            return new ScriptTaskRunResult(
                TaskName: result.TaskCode,
                CorrelationId: result.ExecutionId,
                ScriptsExecuted: result.ScriptsExecuted ?? 0,
                FilesWritten: result.FilesWritten ?? 0,
                OutputPath: result.OutputPath,
                Message: result.Message);
        }
        catch (TaskLaunchException ex)
        {
            _logger.LogWarning("Extra task rejected. Code: {ErrorCode}, Message: {Message}", ex.ErrorCode, ex.Message);
            throw TaskLaunchExceptions.ToApiProblem(ex, "Extra task conflict");
        }
    }
}
