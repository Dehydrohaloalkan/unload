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
    private readonly IRunCoordinator _runCoordinator;
    private readonly IRunStateStore _runStateStore;
    private readonly IHubContext<RunStatusHub> _hubContext;

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
        IRunCoordinator runCoordinator,
        IRunStateStore runStateStore,
        IHubContext<RunStatusHub> hubContext)
    {
        _catalogService = catalogService;
        _orchestrator = orchestrator;
        _runCoordinator = runCoordinator;
        _runStateStore = runStateStore;
        _hubContext = hubContext;
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
        if (request.MemberCodes is null)
        {
            return BadRequest(new { message = "Member codes payload is required." });
        }

        var normalizedMemberCodes = request.MemberCodes
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedMemberCodes.Length == 0)
        {
            return BadRequest(new { message = "At least one member code is required." });
        }

        var catalog = await _catalogService.GetCatalogAsync(cancellationToken);
        var selectedMembers = catalog.Members
            .Where(member => normalizedMemberCodes.Contains(member.Code, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var selectedCodes = selectedMembers.Select(static x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownCodes = normalizedMemberCodes.Where(code => !selectedCodes.Contains(code)).ToArray();
        if (unknownCodes.Length > 0)
        {
            return BadRequest(new
            {
                message = $"Unknown member codes: {string.Join(", ", unknownCodes)}",
                unknownMemberCodes = unknownCodes
            });
        }

        var selectedMemberIds = selectedMembers.Select(static x => x.Id).ToHashSet();
        var targetCodes = catalog.Targets
            .Where(target => selectedMemberIds.Contains(target.MemberId))
            .Select(static target => target.TargetCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (targetCodes.Length == 0)
        {
            return BadRequest(new { message = "No target codes found for selected members." });
        }

        string correlationId;
        try
        {
            correlationId = _orchestrator.StartRun(targetCodes, selectedMembers.Select(static x => x.Name).ToArray());
        }
        catch (RunAlreadyInProgressException ex)
        {
            return Conflict(new
            {
                message = ex.Message,
                activeCorrelationId = ex.ActiveCorrelationId
            });
        }

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
            return NotFound(new { message = "Active run with specified correlationId was not found." });
        }

        _runStateStore.SetCancelled(correlationId, "Run cancellation requested.");
        var state = _runStateStore.Get(correlationId);
        if (state is not null)
        {
            await _hubContext.Clients.All.SendAsync("run_status", state, cancellationToken);
        }

        return Accepted($"/api/runs/{correlationId}", new { correlationId, status = "cancellation_requested" });
    }
}
