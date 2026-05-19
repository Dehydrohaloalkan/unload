using Microsoft.AspNetCore.Mvc;
using Unload.Api.Models;
using Unload.Core;
using Unload.Store;
using Unload.Tasks;
using Unload.Tasks.MainUnload;

namespace Unload.Api.Controllers;

/// <summary>
/// Контроллер чтения каталога и доступных мемберов для запуска выгрузки.
/// </summary>
/// <remarks>
/// Создает контроллер каталога.
/// </remarks>
/// <param name="catalogService">Сервис чтения каталога.</param>
/// <param name="runWorkflow">Single-active workflow активного запуска.</param>
/// <param name="runStateStore">Хранилище статусов запусков.</param>
[ApiController]
[Route("api")]
public class CatalogController(
    ICatalogService catalogService,
    RunActivationChannel runWorkflow,
    RunStateStore runStateStore) : ControllerBase
{
    private readonly ICatalogService _catalogService = catalogService;
    private readonly RunActivationChannel _runWorkflow = runWorkflow;
    private readonly RunStateStore _runStateStore = runStateStore;

    /// <summary>
    /// Возвращает полный каталог групп, мемберов и target-ов.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса.</param>
    /// <returns>Текущее состояние каталога.</returns>
    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalogAsync(CancellationToken cancellationToken)
    {
        var catalog = await _catalogService.GetCatalogAsync(cancellationToken);
        return Ok(catalog);
    }

    /// <summary>
    /// Возвращает список мемберов, доступных для запуска, вместе с target-кодами и активным статусом.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса.</param>
    /// <returns>Нормализованный список мемберов для UI-клиентов.</returns>
    [HttpGet("members")]
    public async Task<IActionResult> GetMembersAsync(CancellationToken cancellationToken)
    {
        var catalog = await _catalogService.GetCatalogAsync(cancellationToken);
        var activeCorrelationId = _runWorkflow.GetActiveCorrelationId();
        var activeRun = string.IsNullOrWhiteSpace(activeCorrelationId)
            ? null
            : _runStateStore.Get(activeCorrelationId);

        var members = catalog.Members
            .OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(member =>
            {
                var targetCodes = catalog.Targets
                    .Where(target => target.MemberId == member.Id)
                    .Select(static target => target.TargetCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                MemberRunStatusInfo? activeStatus = null;
                if (activeRun?.MemberStatuses is not null &&
                    activeRun.MemberStatuses.TryGetValue(member.Name, out var memberStatus))
                {
                    activeStatus = memberStatus;
                }

                return new MemberCatalogItem(
                    member.Code,
                    member.Name,
                    targetCodes,
                    activeRun?.CorrelationId,
                    activeStatus);
            })
            .ToArray();

        return Ok(members);
    }
}
