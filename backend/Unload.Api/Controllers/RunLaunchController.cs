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
    public async Task<IActionResult> StartRunAsync([FromBody] RunStartRequest request, CancellationToken cancellationToken)
    {
        return await ExecuteLaunchAsync("Run conflict", "Run launch", async () =>
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
    public IActionResult GetPresetState()
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
    public async Task<IActionResult> RunPresetAsync([FromBody] AdminTaskRequest? request, CancellationToken cancellationToken)
    {
        return await ExecuteLaunchAsync("Preset task conflict", "Preset task", async () =>
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
    public async Task<IActionResult> GetExtraBanksAsync(CancellationToken cancellationToken)
    {
        var banks = await _extraBanksService.GetBanksAsync(cancellationToken);
        return Ok(banks);
    }

    [HttpPost("extra")]
    public async Task<IActionResult> RunExtraAsync([FromBody] AdminTaskRequest? request, CancellationToken cancellationToken)
    {
        return await ExecuteLaunchAsync("Extra task conflict", "Extra task", async () =>
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
    public async Task<IActionResult> StopRunAsync(string correlationId, CancellationToken cancellationToken)
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

        return Accepted($"/api/runs/{correlationId}", new { correlationId, status = "cancellation_requested" });
    }

    private async Task PublishRunStateAsync(string correlationId, CancellationToken cancellationToken)
    {
        var runState = _runStateStore.Get(correlationId);
        if (runState is not null)
        {
            await _hubContext.Clients.All.SendAsync("run_status", runState, cancellationToken);
        }
    }

    private async Task PublishPresetStateAsync(
        ScriptTaskRunResult scriptResult,
        bool alreadyCompleted,
        CancellationToken cancellationToken)
    {
        await _hubContext.Clients.All.SendAsync("preset_state", _dailyWindowPolicy.Get(), cancellationToken);
        if (alreadyCompleted)
        {
            await _hubContext.Clients.All.SendAsync("preset_replayed", scriptResult, cancellationToken);
        }
    }

    private async Task<IActionResult> ExecuteLaunchAsync(
        string conflictTitle,
        string operationName,
        Func<Task<IActionResult>> launch)
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
            "/hubs/status",
            "SubscribeRun",
            "status",
            "run_status",
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
