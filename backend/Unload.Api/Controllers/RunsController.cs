using Microsoft.AspNetCore.Mvc;
using Unload.Api;
using Microsoft.AspNetCore.SignalR;
using Unload.Api.ErrorHandling;
using Unload.Api.UseCases;
using Unload.Run.Application;
using Unload.TaskFlow;

namespace Unload.Api.Controllers;

/// <summary>
/// Контроллер управления запусками выгрузки.
/// </summary>
[ApiController]
[Route("api/runs")]
public class RunsController : ControllerBase
{
    private readonly IStartRunUseCase _startRunUseCase;
    private readonly IRunPresetUseCase _runPresetUseCase;
    private readonly IRunExtraUseCase _runExtraUseCase;
    private readonly IRunCoordinator _runCoordinator;
    private readonly IRunStateStore _runStateStore;
    private readonly IPresetGateService _presetGateService;
    private readonly IHubContext<RunStatusHub> _hubContext;
    private readonly ILogger<RunsController> _logger;

    /// <summary>
    /// Создает контроллер запусков.
    /// </summary>
    /// <param name="startRunUseCase">Use-case запуска основной выгрузки.</param>
    /// <param name="runPresetUseCase">Use-case запуска preset-задачи.</param>
    /// <param name="runExtraUseCase">Use-case запуска extra-задачи.</param>
    /// <param name="runCoordinator">Координатор активного запуска.</param>
    /// <param name="runStateStore">Хранилище статусов запусков.</param>
    /// <param name="presetGateService">Сервис правил и состояния preset-гейта.</param>
    /// <param name="hubContext">SignalR-контекст трансляции статусов.</param>
    /// <param name="logger">Логгер контроллера.</param>
    public RunsController(
        IStartRunUseCase startRunUseCase,
        IRunPresetUseCase runPresetUseCase,
        IRunExtraUseCase runExtraUseCase,
        IRunCoordinator runCoordinator,
        IRunStateStore runStateStore,
        IPresetGateService presetGateService,
        IHubContext<RunStatusHub> hubContext,
        ILogger<RunsController> logger)
    {
        _startRunUseCase = startRunUseCase;
        _runPresetUseCase = runPresetUseCase;
        _runExtraUseCase = runExtraUseCase;
        _runCoordinator = runCoordinator;
        _runStateStore = runStateStore;
        _presetGateService = presetGateService;
        _hubContext = hubContext;
        _logger = logger;
    }

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
    public async Task<IActionResult> RunPresetAsync(CancellationToken cancellationToken)
    {
        var result = await _runPresetUseCase.ExecuteAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Запускает доп-выгрузку скриптов из корня <c>scripts</c> (без подпапок).
    /// </summary>
    [HttpPost("extra")]
    public async Task<IActionResult> RunExtraAsync(CancellationToken cancellationToken)
    {
        var result = await _runExtraUseCase.ExecuteAsync(cancellationToken);
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

    /// <summary>
    /// Возвращает активный запуск, если он существует.
    /// </summary>
    /// <returns>Статус активного запуска или только его идентификатор.</returns>
    [HttpGet("active")]
    public IActionResult GetActiveRun()
    {
        var correlationId = _runCoordinator.GetActiveCorrelationId();
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
        if (!_runCoordinator.TryCancel(correlationId))
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
