using System.Collections.Concurrent;
using Unload.Core;

namespace Unload.MQ;

/// <summary>
/// In-memory заглушка публикатора MQ-событий.
/// Используется для локальной разработки без внешнего брокера сообщений.
/// </summary>
public class InMemoryMqPublisher : IMqPublisher
{
    private readonly ConcurrentQueue<RunnerEvent> _events = new();

    /// <summary>
    /// Сохраняет событие в локальную очередь в памяти.
    /// </summary>
    /// <param name="event">Событие раннера для публикации.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Завершенная задача после помещения события в очередь.</returns>
    public Task PublishAsync(RunnerEvent @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _events.Enqueue(@event);
        return Task.CompletedTask;
    }
}
