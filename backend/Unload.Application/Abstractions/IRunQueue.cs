using Unload.Core;

namespace Unload.Application;

/// <summary>
/// Контракт очереди запусков между orchestrator и фоновым обработчиком.
/// Используется для асинхронной передачи <see cref="RunRequest"/> на выполнение.
/// </summary>
public interface IRunQueue
{
    /// <summary>
    /// Пытается поставить запуск в очередь без блокировки вызывающего потока.
    /// </summary>
    /// <param name="request">Запрос запуска выгрузки.</param>
    /// <returns><c>true</c>, если запрос поставлен в очередь; иначе <c>false</c>.</returns>
    bool TryEnqueue(RunRequest request);

    /// <summary>
    /// Возвращает непрерывный поток запросов по мере появления в очереди.
    /// </summary>
    /// <param name="cancellationToken">Токен остановки чтения очереди.</param>
    /// <returns>Асинхронный поток запросов на выполнение.</returns>
    IAsyncEnumerable<RunRequest> DequeueAllAsync(CancellationToken cancellationToken);
}
