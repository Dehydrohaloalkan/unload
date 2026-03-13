using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Unload.Api;
using Unload.Application;
using Unload.Core;

namespace Unload.Api.Controllers;

/// <summary>
/// Контроллер управления запусками выгрузки.
/// </summary>
[ApiController]
[Route("api/runs")]
public class RunsController : ControllerBase
{
    private readonly ICatalogService _catalogService;
    private readonly IRunOrchestrator _orchestrator;
    private readonly IScriptTaskOrchestrator _scriptTaskOrchestrator;
    private readonly IRunCoordinator _runCoordinator;
    private readonly IRunStateStore _runStateStore;
    private readonly IPresetGateService _presetGateService;
    private readonly IHubContext<RunStatusHub> _hubContext;
    private readonly ILogger<RunsController> _logger;

    /// <summary>
    /// Создает контроллер запусков.
    /// </summary>
    /// <param name="catalogService">Сервис чтения каталога.</param>
    /// <param name="orchestrator">Оркестратор запуска выгрузки.</param>
    /// <param name="runCoordinator">Координатор активного запуска.</param>
    /// <param name="runStateStore">Хранилище статусов запусков.</param>
    /// <param name="hubContext">SignalR-контекст трансляции статусов.</param>
    public RunsController(
        ICatalogService catalogService,
        IRunOrchestrator orchestrator,
        IScriptTaskOrchestrator scriptTaskOrchestrator,
        IRunCoordinator runCoordinator,
        IRunStateStore runStateStore,
        IPresetGateService presetGateService,
        IHubContext<RunStatusHub> hubContext,
        ILogger<RunsController> logger)
    {
        _catalogService = catalogService;
        _orchestrator = orchestrator;
        _scriptTaskOrchestrator = scriptTaskOrchestrator;
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
        if (!_presetGateService.CanRunMainAndExtra(out var gateReason))
        {
            _logger.LogWarning("Run launch blocked by preset gate. Reason: {Reason}", gateReason);
            return ApiProblem(
                StatusCodes.Status409Conflict,
                "Run is blocked by preset gate",
                gateReason,
                "PRESET_GATE_BLOCKED");
        }

        if (request.MemberCodes is null)
        {
            _logger.LogWarning("Run launch rejected: memberCodes payload is missing.");
            return ApiProblem(
                StatusCodes.Status400BadRequest,
                "Validation error",
                "Member codes payload is required.",
                "VALIDATION_ERROR");
        }

        var normalizedMemberCodes = request.MemberCodes
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedMemberCodes.Length == 0)
        {
            _logger.LogWarning("Run launch rejected: memberCodes became empty after normalization.");
            return ApiProblem(
                StatusCodes.Status400BadRequest,
                "Validation error",
                "At least one member code is required.",
                "VALIDATION_ERROR");
        }

        var catalog = await _catalogService.GetCatalogAsync(cancellationToken);
        var selectedMembers = catalog.Members
            .Where(member => normalizedMemberCodes.Contains(member.Code, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var selectedCodes = selectedMembers.Select(static x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownCodes = normalizedMemberCodes.Where(code => !selectedCodes.Contains(code)).ToArray();
        if (unknownCodes.Length > 0)
        {
            _logger.LogWarning("Run launch rejected: unknown member codes requested: {UnknownCodes}", string.Join(", ", unknownCodes));
            return ApiProblem(
                StatusCodes.Status400BadRequest,
                "Validation error",
                $"Unknown member codes: {string.Join(", ", unknownCodes)}",
                "UNKNOWN_MEMBER_CODES");
        }

        var selectedMemberIds = selectedMembers.Select(static x => x.Id).ToHashSet();
        var targetCodes = catalog.Targets
            .Where(target => selectedMemberIds.Contains(target.MemberId))
            .Select(static target => target.TargetCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (targetCodes.Length == 0)
        {
            _logger.LogWarning("Run launch rejected: no target codes resolved for selected members.");
            return ApiProblem(
                StatusCodes.Status400BadRequest,
                "Validation error",
                "No target codes found for selected members.",
                "TARGET_CODES_NOT_FOUND");
        }

        string correlationId;
        try
        {
            correlationId = _orchestrator.StartRun(targetCodes, selectedMembers.Select(static x => x.Name).ToArray());
        }
        catch (RunAlreadyInProgressException ex)
        {
            _logger.LogWarning("Run launch conflict. ActiveCorrelationId: {ActiveCorrelationId}", ex.ActiveCorrelationId);
            return ApiProblem(
                StatusCodes.Status409Conflict,
                "Run conflict",
                ex.Message,
                "RUN_ALREADY_IN_PROGRESS",
                new Dictionary<string, object?>
                {
                    ["activeCorrelationId"] = ex.ActiveCorrelationId
                });
        }

        _logger.LogInformation(
            "Run accepted. CorrelationId: {CorrelationId}, Members: {MemberCount}, Targets: {TargetCount}",
            correlationId,
            selectedMembers.Length,
            targetCodes.Length);

        var runState = _runStateStore.Get(correlationId);
        if (runState is not null)
        {
            await _hubContext.Clients.All.SendAsync("run_status", runState, cancellationToken);
        }

        return Accepted(
            $"/api/runs/{correlationId}",
            new RunAcceptedResponse(
                correlationId,
                $"/api/runs/{correlationId}",
                "/hubs/status",
                "SubscribeRun",
                "status",
                "run_status",
                $"/api/runs/{correlationId}/stop"));
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
        if (!_presetGateService.CanRunPreset(out var reason))
        {
            _logger.LogWarning("Preset launch blocked. Reason: {Reason}", reason);
            return ApiProblem(
                StatusCodes.Status409Conflict,
                "Preset is not available",
                reason,
                "PRESET_GATE_BLOCKED");
        }

        ScriptTaskRunResult result;
        try
        {
            _logger.LogInformation("Preset task launch requested.");
            result = await _scriptTaskOrchestrator.RunPresetAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Preset task conflict: {Message}", ex.Message);
            return ApiProblem(
                StatusCodes.Status409Conflict,
                "Preset task conflict",
                ex.Message,
                "SCRIPT_TASK_CONFLICT");
        }

        var changed = _presetGateService.MarkPresetCompleted();
        if (changed)
        {
            await _hubContext.Clients.All.SendAsync("preset_state", _presetGateService.Get(), cancellationToken);
        }

        _logger.LogInformation(
            "Preset task completed. CorrelationId: {CorrelationId}, ScriptsExecuted: {ScriptsExecuted}",
            result.CorrelationId,
            result.ScriptsExecuted);
        return Ok(result);
    }

    /// <summary>
    /// Запускает доп-выгрузку скриптов из корня <c>scripts</c> (без подпапок).
    /// </summary>
    [HttpPost("extra")]
    public async Task<IActionResult> RunExtraAsync(CancellationToken cancellationToken)
    {
        if (!_presetGateService.CanRunMainAndExtra(out var gateReason))
        {
            _logger.LogWarning("Extra launch blocked by preset gate. Reason: {Reason}", gateReason);
            return ApiProblem(
                StatusCodes.Status409Conflict,
                "Extra task is blocked by preset gate",
                gateReason,
                "PRESET_GATE_BLOCKED");
        }

        try
        {
            _logger.LogInformation("Extra task launch requested.");
            var result = await _scriptTaskOrchestrator.RunExtraAsync(cancellationToken);
            _logger.LogInformation(
                "Extra task completed. CorrelationId: {CorrelationId}, ScriptsExecuted: {ScriptsExecuted}, FilesWritten: {FilesWritten}",
                result.CorrelationId,
                result.ScriptsExecuted,
                result.FilesWritten);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Extra task conflict: {Message}", ex.Message);
            return ApiProblem(
                StatusCodes.Status409Conflict,
                "Extra task conflict",
                ex.Message,
                "SCRIPT_TASK_CONFLICT");
        }
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
            return NotFound();
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
            return ApiProblem(
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

    private ObjectResult ApiProblem(
        int statusCode,
        string title,
        string detail,
        string errorCode,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Type = $"https://httpstatuses.com/{statusCode}",
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["errorCode"] = errorCode;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        if (extensions is not null)
        {
            foreach (var (key, extensionValue) in extensions)
            {
                problem.Extensions[key] = extensionValue;
            }
        }

        return StatusCode(statusCode, problem);
    }
}
