using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Unload.Api.Abstractions;
using Unload.Api.ErrorHandling;
using Unload.Api.Models;
using Unload.Core;
using Unload.Api.UseCases.Abstractions;
using Unload.Run.Application;
using Unload.TaskFlow;
using Unload.Workflow;

namespace Unload.Api.Controllers;

/// <summary>
/// Контроллер управления запусками выгрузки.
/// </summary>
[ApiController]
[Route("api/runs")]
public class RunsController(
    IStartRunUseCase startRunUseCase,
    IRunPresetUseCase runPresetUseCase,
    IRunExtraUseCase runExtraUseCase,
    ISingleActiveWorkflow<RunRequest> runWorkflow,
    IRunStateStore runStateStore,
    IPresetGateService presetGateService,
    ITaskExecutionHistoryStore taskExecutionHistoryStore,
    HistoryRetentionOptions historyRetentionOptions,
    IHubContext<RunStatusHub> hubContext,
    ILogger<RunsController> logger) : ControllerBase
{
    private readonly IStartRunUseCase _startRunUseCase = startRunUseCase;
    private readonly IRunPresetUseCase _runPresetUseCase = runPresetUseCase;
    private readonly IRunExtraUseCase _runExtraUseCase = runExtraUseCase;
    private readonly ISingleActiveWorkflow<RunRequest> _runWorkflow = runWorkflow;
    private readonly IRunStateStore _runStateStore = runStateStore;
    private readonly IPresetGateService _presetGateService = presetGateService;
    private readonly ITaskExecutionHistoryStore _taskExecutionHistoryStore = taskExecutionHistoryStore;
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
        var response = await _startRunUseCase.ExecuteAsync(request, cancellationToken);
        return Accepted(
            response.RunStatusPath,
            response);
    }

    /// <summary>
    /// Возвращает состояние preset-гейта (расписание, готовность, признак завершения).
    /// </summary>
    [HttpGet("preset/state")]
    public IActionResult GetPresetState()
    {
        var state = _presetGateService.Get();
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
        var result = await _runPresetUseCase.ExecuteAsync(request?.AdminOverride == true, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Запускает доп-выгрузку скриптов из корня <c>scripts</c> (без подпапок).
    /// </summary>
    [HttpPost("extra")]
    public async Task<IActionResult> RunExtraAsync([FromBody] AdminTaskRequest? request, CancellationToken cancellationToken)
    {
        var result = await _runExtraUseCase.ExecuteAsync(request?.AdminOverride == true, cancellationToken);
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
                string.Equals(run.TaskCode, WorkflowTaskCodes.Run, StringComparison.OrdinalIgnoreCase) &&
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
            .FirstOrDefault(record => string.Equals(record.TaskCode, WorkflowTaskCodes.Run, StringComparison.OrdinalIgnoreCase))
            ?.CompletedAt;
        var extraLastCompletedAt = history
            .FirstOrDefault(record => string.Equals(record.TaskCode, WorkflowTaskCodes.Extra, StringComparison.OrdinalIgnoreCase))
            ?.CompletedAt;

        return Ok(new WorkflowDashboardSnapshotResponse(
            _presetGateService.Get(),
            _taskExecutionHistoryStore.HasRunToday(WorkflowTaskCodes.Run, today),
            _taskExecutionHistoryStore.HasRunToday(WorkflowTaskCodes.Extra, today),
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
