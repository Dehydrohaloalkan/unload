using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Unload.Api.ErrorHandling;
using Unload.Api.Models;
using Unload.Store;
using Unload.Tasks;
using Unload.Tasks.MainUnload;

namespace Unload.Api.Controllers;

/// <summary>
/// Контроллер управления запусками выгрузки.
/// </summary>
[ApiController]
[Route("api/runs")]
public class RunsController(
    TaskWorkflow taskWorkflow,
    RequeueService requeueService,
    RunActivationChannel runWorkflow,
    RunStateStore runStateStore,
    DailyWindowPolicy dailyWindowPolicy,
    TaskExecutionHistoryStore taskExecutionHistoryStore,
    HistoryRetentionOptions historyRetentionOptions,
    IHubContext<RunStatusHub> hubContext,
    ILogger<RunsController> logger) : ControllerBase
{
    private readonly TaskWorkflow _taskWorkflow = taskWorkflow;
    private readonly RequeueService _requeueService = requeueService;
    private readonly RunActivationChannel _runWorkflow = runWorkflow;
    private readonly RunStateStore _runStateStore = runStateStore;
    private readonly DailyWindowPolicy _dailyWindowPolicy = dailyWindowPolicy;
    private readonly TaskExecutionHistoryStore _taskExecutionHistoryStore = taskExecutionHistoryStore;
    private readonly HistoryRetentionOptions _historyRetentionOptions = historyRetentionOptions;
    private readonly IHubContext<RunStatusHub> _hubContext = hubContext;
    private readonly ILogger<RunsController> _logger = logger;

    /// <summary>
    /// Создает новый запуск выгрузки по выбранным кодам мемберов.
    /// </summary>
    /// <param name="request">Запрос на запуск с набором кодов мемберов.</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса.</param>
    /// <returns>Информация о созданном запуске и каналах отслеживания статуса.</returns>
    [HttpPost]
    public async Task<IActionResult> StartRunAsync([FromBody] RunStartRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var selectedTargetCodes = request.TargetCodes?
                .Where(static code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
            var selectionMode = selectedTargetCodes.Length > 0
                ? RunSelectionMode.TargetCodes
                : RunSelectionMode.MemberCodes;
            var selectedCodes = selectionMode == RunSelectionMode.TargetCodes
                ? selectedTargetCodes
                : request.MemberCodes;

            var result = await _taskWorkflow.LaunchAsync(
                new TaskLaunchRequest(
                    TaskCode: TaskCodes.Run,
                    AdminOverride: request.AdminOverride,
                    PublishToGateway: request.PublishToGateway,
                    Codes: selectedCodes,
                    SelectionMode: selectionMode),
                cancellationToken);

            _logger.LogInformation("Run accepted. CorrelationId: {CorrelationId}", result.ExecutionId);

            var runState = _runStateStore.Get(result.ExecutionId);
            if (runState is not null)
            {
                await _hubContext.Clients.All.SendAsync("run_status", runState, cancellationToken);
            }

            var response = new RunAcceptedResponse(
                result.ExecutionId,
                $"/api/runs/{result.ExecutionId}",
                "/hubs/status",
                "SubscribeRun",
                "status",
                "run_status",
                $"/api/runs/{result.ExecutionId}/stop");

            return Accepted(response.RunStatusPath, response);
        }
        catch (TaskLaunchException ex)
        {
            _logger.LogWarning("Run launch rejected. Code: {ErrorCode}, Message: {Message}", ex.ErrorCode, ex.Message);
            throw TaskLaunchExceptions.ToApiProblem(ex, "Run conflict");
        }
    }

    /// <summary>
    /// Возвращает состояние preset-гейта (расписание, готовность, признак завершения).
    /// </summary>
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

    /// <summary>
    /// Запускает preset-задачу (скрипты из <c>scripts/preset</c>).
    /// </summary>
    [HttpPost("preset")]
    public async Task<IActionResult> RunPresetAsync([FromBody] AdminTaskRequest? request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Preset task launch requested.");
            var alreadyCompleted = _dailyWindowPolicy.Get().PresetCompleted;
            var result = await _taskWorkflow.LaunchAsync(
                new TaskLaunchRequest(TaskCode: TaskCodes.Preset, AdminOverride: request?.AdminOverride == true),
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
            return Ok(scriptResult);
        }
        catch (TaskLaunchException ex)
        {
            _logger.LogWarning("Preset task rejected. Code: {ErrorCode}, Message: {Message}", ex.ErrorCode, ex.Message);
            throw TaskLaunchExceptions.ToApiProblem(ex, "Preset task conflict");
        }
    }

    /// <summary>
    /// Запускает доп-выгрузку скриптов из корня <c>scripts</c> (без подпапок).
    /// </summary>
    [HttpPost("extra")]
    public async Task<IActionResult> RunExtraAsync([FromBody] AdminTaskRequest? request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Extra task launch requested.");
            var result = await _taskWorkflow.LaunchAsync(
                new TaskLaunchRequest(
                    TaskCode: TaskCodes.Extra,
                    AdminOverride: request?.AdminOverride == true,
                    PublishToGateway: request?.PublishToGateway != false),
                cancellationToken);

            _logger.LogInformation(
                "Extra task completed. CorrelationId: {CorrelationId}, ScriptsExecuted: {ScriptsExecuted}, FilesWritten: {FilesWritten}",
                result.ExecutionId,
                result.ScriptsExecuted,
                result.FilesWritten);

            return Ok(new ScriptTaskRunResult(
                TaskName: result.TaskCode,
                CorrelationId: result.ExecutionId,
                ScriptsExecuted: result.ScriptsExecuted ?? 0,
                FilesWritten: result.FilesWritten ?? 0,
                OutputPath: result.OutputPath,
                Message: result.Message));
        }
        catch (TaskLaunchException ex)
        {
            _logger.LogWarning("Extra task rejected. Code: {ErrorCode}, Message: {Message}", ex.ErrorCode, ex.Message);
            throw TaskLaunchExceptions.ToApiProblem(ex, "Extra task conflict");
        }
    }

    /// <summary>
    /// Повторно публикует результаты прошлых запусков в шлюз (массово).
    /// </summary>
    [HttpPost("requeue")]
    public async Task<IActionResult> RequeueToGatewayAsync([FromBody] RequeueToGatewayRequest request, CancellationToken cancellationToken)
    {
        var result = await _requeueService.ExecuteAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Возвращает историю запусков и их текущие статусы.
    /// </summary>
    /// <returns>Список запусков из in-memory хранилища.</returns>
    [HttpGet]
    public IActionResult GetRuns()
    {
        return Ok(_runStateStore.List());
    }

    [HttpGet("today")]
    public IActionResult GetTodayRuns()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var runs = _runStateStore
            .List()
            .Where(run =>
                string.Equals(run.TaskCode, TaskCodes.Run, StringComparison.OrdinalIgnoreCase) &&
                DateOnly.FromDateTime(run.CreatedAt.LocalDateTime) == today)
            .OrderByDescending(static run => run.CreatedAt)
            .ToArray();
        return Ok(runs);
    }

    [HttpGet("dashboard")]
    public IActionResult GetWorkflowDashboard()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var history = _taskExecutionHistoryStore.List(today);
        var runLastCompletedAt = history
            .FirstOrDefault(record => string.Equals(record.TaskCode, TaskCodes.Run, StringComparison.OrdinalIgnoreCase))
            ?.CompletedAt;
        var extraLastCompletedAt = history
            .FirstOrDefault(record => string.Equals(record.TaskCode, TaskCodes.Extra, StringComparison.OrdinalIgnoreCase))
            ?.CompletedAt;

        return Ok(new WorkflowDashboardSnapshotResponse(
            _dailyWindowPolicy.Get(),
            _taskExecutionHistoryStore.HasRunToday(TaskCodes.Run, today),
            _taskExecutionHistoryStore.HasRunToday(TaskCodes.Extra, today),
            runLastCompletedAt,
            extraLastCompletedAt,
            history));
    }

    /// <summary>
    /// Возвращает историю RUN + PRESET + EXTRA за последние N дней.
    /// По умолчанию N берётся из настроек HistoryRetention:RetentionDays.
    /// </summary>
    [HttpGet("history")]
    public IActionResult GetWorkflowHistory([FromQuery] int? days)
    {
        var effectiveDays = days ?? _historyRetentionOptions.RetentionDays;
        effectiveDays = Math.Clamp(effectiveDays, 1, 365);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var fromDay = today.AddDays(-(effectiveDays - 1));

        var runs = _runStateStore
            .List()
            .Where(run =>
            {
                var day = DateOnly.FromDateTime(run.CreatedAt.LocalDateTime);
                return day >= fromDay && day <= today;
            })
            .OrderByDescending(static run => run.CreatedAt)
            .ToArray();

        var taskHistory = _taskExecutionHistoryStore.ListRange(fromDay, today);

        return Ok(new WorkflowHistoryResponse(
            FromDayInclusive: fromDay,
            ToDayInclusive: today,
            Days: effectiveDays,
            Runs: runs,
            TaskHistory: taskHistory));
    }

    /// <summary>
    /// Возвращает активный запуск, если он существует.
    /// </summary>
    /// <returns>Статус активного запуска или только его идентификатор.</returns>
    [HttpGet("active")]
    public IActionResult GetActiveRun()
    {
        var correlationId = _runWorkflow.GetActiveCorrelationId();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return Ok(new { correlationId = (string?)null });
        }

        var run = _runStateStore.Get(correlationId);
        return run is null
            ? Ok(new { correlationId })
            : Ok(run);
    }

    /// <summary>
    /// Возвращает статус запуска по идентификатору корреляции.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <returns>Состояние запуска либо 404, если запуск не найден.</returns>
    [HttpGet("{correlationId}")]
    public IActionResult GetRunByCorrelationId(string correlationId)
    {
        var run = _runStateStore.Get(correlationId);
        return run is null ? NotFound() : Ok(run);
    }

    /// <summary>
    /// Запрашивает остановку активного запуска по идентификатору корреляции.
    /// </summary>
    /// <param name="correlationId">Идентификатор активного запуска.</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса.</param>
    /// <returns>Подтверждение запроса на отмену.</returns>
    [HttpPost("{correlationId}/stop")]
    public async Task<IActionResult> StopRunAsync(string correlationId, CancellationToken cancellationToken)
    {
        if (!_runWorkflow.TryCancel(correlationId))
        {
            throw new ApiProblemException(
                StatusCodes.Status404NotFound,
                "Run was not found",
                "Active run with specified correlationId was not found.",
                "RUN_NOT_FOUND");
        }

        _runStateStore.SetCancellationRequested(correlationId, "Run cancellation requested.");
        _logger.LogInformation("Run cancellation requested. CorrelationId: {CorrelationId}", correlationId);
        var state = _runStateStore.Get(correlationId);
        if (state is not null)
        {
            await _hubContext.Clients.All.SendAsync("run_status", state, cancellationToken);
        }

        return Accepted($"/api/runs/{correlationId}", new { correlationId, status = "cancellation_requested" });
    }
}
