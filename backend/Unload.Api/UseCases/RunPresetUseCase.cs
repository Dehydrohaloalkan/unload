using Microsoft.AspNetCore.SignalR;
using Unload.Api.ErrorHandling;
using Unload.Application;
using Unload.Workflow;

namespace Unload.Api.UseCases;

public interface IRunPresetUseCase
{
    Task<ScriptTaskRunResult> ExecuteAsync(CancellationToken cancellationToken);
}

public sealed class RunPresetUseCase : IRunPresetUseCase
{
    private readonly IWorkflowTaskDispatcher _dispatcher;
    private readonly IPresetGateService _presetGateService;
    private readonly IHubContext<RunStatusHub> _hubContext;
    private readonly ILogger<RunPresetUseCase> _logger;

    public RunPresetUseCase(
        IWorkflowTaskDispatcher dispatcher,
        IPresetGateService presetGateService,
        IHubContext<RunStatusHub> hubContext,
        ILogger<RunPresetUseCase> logger)
    {
        _dispatcher = dispatcher;
        _presetGateService = presetGateService;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task<ScriptTaskRunResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Preset task launch requested.");
            var result = await _dispatcher.DispatchAsync<EmptyWorkflowTaskRequest, ScriptTaskRunResult>(
                WorkflowTaskCodes.Preset,
                new EmptyWorkflowTaskRequest(),
                cancellationToken);
            await _hubContext.Clients.All.SendAsync("preset_state", _presetGateService.Get(), cancellationToken);
            _logger.LogInformation(
                "Preset task completed. CorrelationId: {CorrelationId}, ScriptsExecuted: {ScriptsExecuted}",
                result.CorrelationId,
                result.ScriptsExecuted);
            return result;
        }
        catch (WorkflowTaskDispatchException ex)
        {
            _logger.LogWarning("Preset task rejected. Code: {ErrorCode}, Message: {Message}", ex.ErrorCode, ex.Message);
            throw new ApiProblemException(
                ex.FailureKind == WorkflowTaskFailureKind.Validation
                    ? StatusCodes.Status400BadRequest
                    : StatusCodes.Status409Conflict,
                ex.FailureKind == WorkflowTaskFailureKind.Validation
                    ? "Validation error"
                    : "Preset task conflict",
                ex.Message,
                ex.ErrorCode,
                ex.Extensions);
        }
    }
}
