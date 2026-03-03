namespace Unload.Api;

/// <summary>
/// Контракт HTTP-запроса на запуск выгрузки.
/// Используется endpoint-ом <c>POST /api/runs</c>.
/// </summary>
/// <param name="ProfileCodes">Список кодов профилей, выбранных клиентом.</param>
public record RunStartRequest(IReadOnlyCollection<string> ProfileCodes);

/// <summary>
/// Контракт ответа на успешную постановку запуска в очередь.
/// Используется клиентом для дальнейшего чтения статуса и подписки на SignalR.
/// </summary>
/// <param name="CorrelationId">Идентификатор созданного запуска.</param>
/// <param name="RunStatusPath">Путь API для получения статуса конкретного запуска.</param>
/// <param name="HubPath">Путь SignalR hub для подписки на события.</param>
/// <param name="SubscribeMethod">Имя метода hub для подписки на запуск.</param>
/// <param name="EventName">Имя SignalR-события по шагам раннера.</param>
/// <param name="RunStatusEventName">Имя SignalR-события с агрегированным статусом запуска.</param>
public record RunAcceptedResponse(
    string CorrelationId,
    string RunStatusPath,
    string HubPath,
    string SubscribeMethod,
    string EventName,
    string RunStatusEventName);
