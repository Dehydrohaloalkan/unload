namespace Unload.Core;

/// <summary>
/// Контракт публикации событий раннера в очередь сообщений.
/// Используется как точка расширения для интеграции с реальным MQ.
/// </summary>
public interface IMqPublisher
{
    /// <summary>
    /// Публикует событие выполнения выгрузки в транспорт MQ.
    /// </summary>
    /// <param name="event">Событие раннера для публикации.</param>
    /// <param name="cancellationToken">Токен отмены публикации.</param>
    /// <returns>Задача завершения публикации.</returns>
    Task PublishAsync(RunnerEvent @event, CancellationToken cancellationToken);
}
