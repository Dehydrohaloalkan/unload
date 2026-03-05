namespace Unload.Api;

/// <summary>
/// Контракт HTTP-запроса на запуск выгрузки.
/// Используется endpoint-ом <c>POST /api/runs</c>.
/// </summary>
/// <param name="MemberCodes">Список кодов мемберов, выбранных клиентом.</param>
public record RunStartRequest(IReadOnlyCollection<string> MemberCodes);

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
public record MemberCatalogItem(string Code, string Name, IReadOnlyCollection<string> TargetCodes);
