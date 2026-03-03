namespace Unload.Core;

/// <summary>
/// Контракт движка выполнения выгрузки.
/// Используется application/API/console слоями для запуска обработки и получения потока событий.
/// </summary>
public interface IRunner
{
    /// <summary>
    /// Запускает конвейер выгрузки и возвращает поток событий выполнения.
    /// </summary>
    /// <param name="request">Запрос с профилями и параметрами запуска.</param>
    /// <param name="cancellationToken">Токен отмены выполнения.</param>
    /// <returns>Асинхронный поток <see cref="RunnerEvent"/> до завершения запуска.</returns>
    IAsyncEnumerable<RunnerEvent> RunAsync(RunRequest request, CancellationToken cancellationToken);
}
