using Microsoft.AspNetCore.SignalR;

namespace Unload.Api;

/// <summary>
/// SignalR hub для подписки клиентов на события конкретных запусков.
/// Используется фронтендом/клиентами для получения live-статусов выгрузки.
/// </summary>
public class RunStatusHub : Hub
{
    /// <summary>
    /// Подписывает текущее соединение на группу событий указанного запуска.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <returns>Задача завершения подписки на группу.</returns>
    public Task SubscribeRun(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new HubException("CorrelationId is required.");
        }

        return Groups.AddToGroupAsync(Context.ConnectionId, correlationId.Trim());
    }

    /// <summary>
    /// Отписывает текущее соединение от группы событий указанного запуска.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <returns>Задача завершения отписки от группы.</returns>
    public Task UnsubscribeRun(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new HubException("CorrelationId is required.");
        }

        return Groups.RemoveFromGroupAsync(Context.ConnectionId, correlationId.Trim());
    }
}
