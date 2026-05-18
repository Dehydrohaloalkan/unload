using Microsoft.AspNetCore.SignalR;
using Unload.Api.ErrorHandling;
using Unload.Api.UseCases.Abstractions;
using Unload.Tasks;

namespace Unload.Api.UseCases;

public class RunPresetUseCase(
    TaskWorkflow taskWorkflow,
    DailyWindowPolicy dailyWindowPolicy,
    IHubContext<RunStatusHub> hubContext,
    ILogger<RunPresetUseCase> logger) : IRunPresetUseCase
{
    private readonly TaskWorkflow _taskWorkflow = taskWorkflow;
    private readonly DailyWindowPolicy _dailyWindowPolicy = dailyWindowPolicy;
    private readonly IHubContext<RunStatusHub> _hubContext = hubContext;
    private readonly ILogger<RunPresetUseCase> _logger = logger;

    public async Task<ScriptTaskRunResult> ExecuteAsync(bool adminOverride, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Preset task launch requested.");
            var alreadyCompleted = _dailyWindowPolicy.Get().PresetCompleted;
            var result = await _taskWorkflow.LaunchAsync(
                new TaskLaunchRequest(TaskCode: TaskCodes.Preset, AdminOverride: adminOverride),
                cancellationToken);

            var scriptResult = new ScriptTaskRunResult(
                TaskName: result.TaskCode,
                CorrelationId: result.ExecutionId,
                ScriptsExecuted: result.ScriptsExecuted ?? 0,
                FilesWritten: result.FilesWritten ?? 0,
                OutputPath: result.OutputPath,
                Message: result.Message);

            await _hubContext.Clients.All.SendAsync("preset_state", _dailyWindowPolicy.Get(), cancellationToken);
            if (alreadyCompleted)
            {
                await _hubContext.Clients.All.SendAsync("preset_replayed", scriptResult, cancellationToken);
            }

            _logger.LogInformation(
                "Preset task completed. CorrelationId: {CorrelationId}, ScriptsExecuted: {ScriptsExecuted}",
                result.ExecutionId,
                result.ScriptsExecuted);
            return scriptResult;
        }
        catch (TaskLaunchException ex)
        {
            _logger.LogWarning("Preset task rejected. Code: {ErrorCode}, Message: {Message}", ex.ErrorCode, ex.Message);
            throw TaskLaunchExceptions.ToApiProblem(ex, "Preset task conflict");
        }
    }
}
