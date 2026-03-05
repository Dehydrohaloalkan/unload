using Unload.Core;

namespace Unload.Application;

/// <summary>
/// Контракт координатора запусков с ограничением на один активный run.
/// Принимает активации и отдает их фоновому обработчику.
/// </summary>
public interface IRunCoordinator
{
    /// <summary>
    /// Пытается активировать новый запуск.
    /// </summary>
    /// <param name="request">Запрос запуска раннера.</param>
    /// <returns><c>true</c>, если запуск принят; иначе <c>false</c>.</returns>
    bool TryActivate(RunRequest request);

    /// <summary>
    /// Возвращает асинхронный поток принятых активаций.
    /// </summary>
    /// <param name="cancellationToken">Токен остановки чтения активаций.</param>
    /// <returns>Поток запросов запуска для background worker.</returns>
    IAsyncEnumerable<RunRequest> ReadActivationsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Освобождает слот активного запуска после завершения.
    /// </summary>
    /// <param name="correlationId">Идентификатор завершенного запуска.</param>
    void Complete(string correlationId);

    /// <summary>
    /// Возвращает correlation id текущего активного запуска.
    /// </summary>
    /// <returns>Идентификатор активного запуска или <c>null</c>.</returns>
    string? GetActiveCorrelationId();
}
