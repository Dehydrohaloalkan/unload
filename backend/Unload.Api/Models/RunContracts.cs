using Unload.Run.Application;

namespace Unload.Api.Models;

/// <summary>
/// Контракт HTTP-запроса на запуск выгрузки.
/// Используется endpoint-ом <c>POST /api/runs</c>.
/// </summary>
/// <param name="MemberCodes">Список кодов мемберов, выбранных клиентом.</param>
public record RunStartRequest(
    IReadOnlyCollection<string> MemberCodes,
    IReadOnlyCollection<string>? TargetCodes = null,
    bool AdminOverride = false,
    bool PublishToGateway = true);

/// <summary>
/// Контракт ответа на успешный запуск выгрузки.
/// Используется клиентом для дальнейшего чтения статуса и подписки на SignalR.
/// </summary>
/// <param name="CorrelationId">Идентификатор созданного запуска.</param>
/// <param name="RunStatusPath">Путь API для получения статуса конкретного запуска.</param>
/// <param name="HubPath">Путь SignalR hub для подписки на события.</param>
/// <param name="SubscribeMethod">Имя метода hub для подписки на запуск.</param>
/// <param name="EventName">Имя SignalR-события по шагам раннера.</param>
/// <param name="RunStatusEventName">Имя SignalR-события с агрегированным статусом запуска.</param>
/// <param name="StopPath">Путь API для остановки конкретного запуска.</param>
public record RunAcceptedResponse(
    string CorrelationId,
    string RunStatusPath,
    string HubPath,
    string SubscribeMethod,
    string EventName,
    string RunStatusEventName,
    string StopPath);

/// <summary>
/// Контракт мембера для запуска выгрузки.
/// </summary>
/// <param name="Code">Код мембера.</param>
/// <param name="Name">Отображаемое имя мембера.</param>
/// <param name="TargetCodes">Список target-кодов, которые будут обработаны для мембера.</param>
/// <param name="ActiveRunCorrelationId">Идентификатор активного запуска, если есть.</param>
/// <param name="ActiveRunStatus">Текущий статус мембера в активном запуске, если есть.</param>
public record MemberCatalogItem(
    string Code,
    string Name,
    IReadOnlyCollection<string> TargetCodes,
    string? ActiveRunCorrelationId,
    MemberRunStatusInfo? ActiveRunStatus);

/// <summary>
/// Контракт запроса запуска задачи с опциональным admin override.
/// </summary>
/// <param name="AdminOverride">Признак обхода стандартных gate/dependency проверок.</param>
public record AdminTaskRequest(bool AdminOverride = false, bool PublishToGateway = true);
