using Microsoft.AspNetCore.SignalR;
using Unload.Api.ErrorHandling;
using Unload.Api.Abstractions;
using Unload.Api.UseCases.Abstractions;
using Unload.TaskFlow;
using Unload.TaskFlow.Exceptions;
using Unload.Workflow;

namespace Unload.Api.UseCases;

public class RunPresetUseCase(
    IWorkflowTaskDispatcher dispatcher,
    IPresetGateService presetGateService,
    ITaskExecutionHistoryStore taskExecutionHistoryStore,
    IHubContext<RunStatusHub> hubContext,
    ILogger<RunPresetUseCase> logger) : IRunPresetUseCase
{
    private readonly IWorkflowTaskDispatcher _dispatcher = dispatcher;
    private readonly IPresetGateService _presetGateService = presetGateService;
    private readonly ITaskExecutionHistoryStore _taskExecutionHistoryStore = taskExecutionHistoryStore;
    private readonly IHubContext<RunStatusHub> _hubContext = hubContext;
    private readonly ILogger<RunPresetUseCase> _logger = logger;

    public async Task<ScriptTaskRunResult> ExecuteAsync(bool adminOverride, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            _logger.LogInformation("Preset task launch requested.");
            var presetState = _presetGateService.Get();
            var alreadyCompleted = presetState.PresetCompleted;
            var result = await _dispatcher.DispatchAsync<EmptyWorkflowTaskRequest, ScriptTaskRunResult>(
                WorkflowTaskCodes.Preset,
                new EmptyWorkflowTaskRequest(adminOverride),
                cancellationToken);
            _taskExecutionHistoryStore.Add(
                WorkflowTaskCodes.Preset,
                startedAt,
                DateTimeOffset.UtcNow,
                result.CorrelationId,
                result.Message,
                result.ScriptsExecuted,
                result.FilesWritten,
                result.OutputPath);
            await _hubContext.Clients.All.SendAsync("preset_state", _presetGateService.Get(), cancellationToken);
            if (alreadyCompleted)
            {
                await _hubContext.Clients.All.SendAsync("preset_replayed", result, cancellationToken);
            }
            _logger.LogInformation(
                "Preset task completed. CorrelationId: {CorrelationId}, ScriptsExecuted: {ScriptsExecuted}",
                result.CorrelationId,
                result.ScriptsExecuted);
            return result;
        }
        catch (WorkflowTaskDispatchException ex)
        {
            _logger.LogWarning("Preset task rejected. Code: {ErrorCode}, Message: {Message}", ex.ErrorCode, ex.Message);
            throw WorkflowDispatchExceptions.ToApiProblem(ex, "Preset task conflict");
        }
    }
}
