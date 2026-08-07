using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Unload.Api.ErrorHandling;
using Unload.Api.Models;
using Unload.Bootstrapper;
using Unload.Store;
using Unload.Tasks;
using Unload.Tasks.ExtraUnload;
using Unload.Tasks.MainUnload;

namespace Unload.Api.Controllers;

/// <summary>
/// Запуск и отмена main, preset и extra операций.
/// </summary>
[ApiController]
[Route("api/runs")]
public class RunLaunchController(
    TaskWorkflow taskWorkflow,
    RunActivationChannel runWorkflow,
    ExtraActivationChannel extraWorkflow,
    RunStateStore runStateStore,
    DailyWindowPolicy dailyWindowPolicy,
    ExtraBanksService extraBanksService,
    IHubContext<RunStatusHub> hubContext,
    ILogger<RunLaunchController> logger) : ControllerBase
{
    private readonly TaskWorkflow _taskWorkflow = taskWorkflow;
    private readonly RunActivationChannel _runWorkflow = runWorkflow;
    private readonly ExtraActivationChannel _extraWorkflow = extraWorkflow;
    private readonly RunStateStore _runStateStore = runStateStore;
    private readonly DailyWindowPolicy _dailyWindowPolicy = dailyWindowPolicy;
    private readonly ExtraBanksService _extraBanksService = extraBanksService;
    private readonly IHubContext<RunStatusHub> _hubContext = hubContext;
    private readonly ILogger<RunLaunchController> _logger = logger;

    [HttpPost]
    [ProducesResponseType<RunAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RunAcceptedResponse>> StartRunAsync(
        [FromBody] RunStartRequest request,
        CancellationToken cancellationToken)
    {
        return await ExecuteLaunchAsync<RunAcceptedResponse>("Run conflict", "Run launch", async () =>
        {
            var result = await _taskWorkflow.LaunchAsync(
                CreateRunLaunchRequest(request),
                cancellationToken);

            _logger.LogInformation("Run accepted. CorrelationId: {CorrelationId}", result.ExecutionId);
            await PublishRunStateAsync(result.ExecutionId, cancellationToken);

            var response = CreateAcceptedResponse(result.ExecutionId);
            return Accepted(response.RunStatusPath, response);
        });
    }

    [HttpGet("preset/state")]
    public ActionResult<PresetGateState> GetPresetState()
    {
        var state = _dailyWindowPolicy.Get();
        _logger.LogDebug(
            "Preset state requested. Enabled: {Enabled}, PollingStarted: {PollingStarted}, ReadyForPreset: {ReadyForPreset}, PresetCompleted: {PresetCompleted}",
            state.Enabled,
            state.PollingStarted,
            state.ReadyForPreset,
            state.PresetCompleted);
        return Ok(state);
    }

    [HttpPost("preset")]
    [ProducesResponseType<ScriptTaskRunResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ScriptTaskRunResult>> RunPresetAsync(
        [FromBody] AdminTaskRequest? request,
        CancellationToken cancellationToken)
    {
        return await ExecuteLaunchAsync<ScriptTaskRunResult>(
            "Preset task conflict",
            "Preset task",
            async () =>
        {
            _logger.LogInformation("Preset task launch requested.");
            var alreadyCompleted = _dailyWindowPolicy.Get().PresetCompleted;
            var result = await _taskWorkflow.LaunchAsync(
                new TaskLaunchRequest(TaskCode: TaskCodes.Preset, AdminOverride: request?.AdminOverride == true),
                cancellationToken);

            var scriptResult = CreateScriptTaskResult(result);
            await PublishPresetStateAsync(scriptResult, alreadyCompleted, cancellationToken);

            _logger.LogInformation(
                "Preset task completed. CorrelationId: {CorrelationId}, ScriptsExecuted: {ScriptsExecuted}",
                result.ExecutionId,
                result.ScriptsExecuted);
            return Ok(scriptResult);
        });
    }

    [HttpGet("extra/banks")]
    public async Task<ActionResult<IReadOnlyList<ExtraBankInfo>>> GetExtraBanksAsync(
        CancellationToken cancellationToken)
    {
        var banks = await _extraBanksService.GetBanksAsync(cancellationToken);
        return Ok(banks);
    }

    [HttpPost("extra")]
    [ProducesResponseType<RunAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RunAcceptedResponse>> RunExtraAsync(
        [FromBody] AdminTaskRequest? request,
        CancellationToken cancellationToken)
    {
        return await ExecuteLaunchAsync<RunAcceptedResponse>(
            "Extra task conflict",
            "Extra task",
            async () =>
        {
            _logger.LogInformation("Extra task launch requested.");
            var result = await _taskWorkflow.LaunchAsync(
                CreateExtraLaunchRequest(request),
                cancellationToken);

            _logger.LogInformation("Extra accepted. CorrelationId: {CorrelationId}", result.ExecutionId);
            await PublishRunStateAsync(result.ExecutionId, cancellationToken);

            var response = CreateAcceptedResponse(result.ExecutionId);
            return Accepted(response.RunStatusPath, response);
        });
    }

    [HttpPost("{correlationId}/stop")]
    [ProducesResponseType<RunCancellationAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RunCancellationAcceptedResponse>> StopRunAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        var cancelled = correlationId.StartsWith("extra-", StringComparison.OrdinalIgnoreCase)
            ? _extraWorkflow.TryCancel(correlationId)
            : _runWorkflow.TryCancel(correlationId);

        if (!cancelled)
        {
            throw new ApiProblemException(
                StatusCodes.Status404NotFound,
                "Run was not found",
                "Active run with specified correlationId was not found.",
                "RUN_NOT_FOUND");
        }

        _runStateStore.SetCancellationRequested(correlationId, "Run cancellation requested.");
        _logger.LogInformation("Run cancellation requested. CorrelationId: {CorrelationId}", correlationId);
        await PublishRunStateAsync(correlationId, cancellationToken);

        return Accepted(
            $"/api/runs/{correlationId}",
            new RunCancellationAcceptedResponse(correlationId, "cancellation_requested"));
    }

    private async Task PublishRunStateAsync(string correlationId, CancellationToken cancellationToken)
    {
        var runState = _runStateStore.Get(correlationId);
        if (runState is not null)
        {
            await _hubContext.Clients.All.SendRunStatusAsync(runState, cancellationToken);
        }
    }

    private async Task PublishPresetStateAsync(
        ScriptTaskRunResult scriptResult,
        bool alreadyCompleted,
        CancellationToken cancellationToken)
    {
        await _hubContext.Clients.All.SendPresetStateAsync(_dailyWindowPolicy.Get(), cancellationToken);
        if (alreadyCompleted)
        {
            await _hubContext.Clients.All.SendPresetReplayedAsync(scriptResult, cancellationToken);
        }
    }

    private async Task<ActionResult<TResponse>> ExecuteLaunchAsync<TResponse>(
        string conflictTitle,
        string operationName,
        Func<Task<ActionResult<TResponse>>> launch)
    {
        try
        {
            return await launch();
        }
        catch (TaskLaunchException ex)
        {
            _logger.LogWarning(
                "{OperationName} rejected. Code: {ErrorCode}, Message: {Message}",
                operationName,
                ex.ErrorCode,
                ex.Message);
            throw TaskLaunchExceptions.ToApiProblem(ex, conflictTitle);
        }
    }

    private static RunAcceptedResponse CreateAcceptedResponse(string correlationId)
    {
        return new RunAcceptedResponse(
            correlationId,
            $"/api/runs/{correlationId}",
            RunStatusHubContract.HubPath,
            RunStatusHubContract.SubscribeMethod,
            RunStatusHubContract.StatusEvent,
            RunStatusHubContract.RunStatusEvent,
            $"/api/runs/{correlationId}/stop");
    }

    private static TaskLaunchRequest CreateRunLaunchRequest(RunStartRequest request)
    {
        var targetCodes = request.TargetCodes?
            .Where(static code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        var selectionMode = targetCodes.Length > 0
            ? RunSelectionMode.TargetCodes
            : RunSelectionMode.MemberCodes;

        return new TaskLaunchRequest(
            TaskCode: TaskCodes.Run,
            AdminOverride: request.AdminOverride,
            PublishToGateway: request.PublishToGateway,
            Codes: selectionMode == RunSelectionMode.TargetCodes ? targetCodes : request.MemberCodes,
            SelectionMode: selectionMode);
    }

    private static TaskLaunchRequest CreateExtraLaunchRequest(AdminTaskRequest? request)
    {
        return new TaskLaunchRequest(
            TaskCode: TaskCodes.Extra,
            AdminOverride: request?.AdminOverride == true,
            PublishToGateway: request?.PublishToGateway != false,
            SelectedBanks: request?.SelectedBanks);
    }

    private static ScriptTaskRunResult CreateScriptTaskResult(TaskExecutionResult result)
    {
        return new ScriptTaskRunResult(
            TaskName: result.TaskCode,
            CorrelationId: result.ExecutionId,
            ScriptsExecuted: result.ScriptsExecuted ?? 0,
            FilesWritten: result.FilesWritten ?? 0,
            OutputPath: result.OutputPath,
            Message: result.Message);
    }
}
