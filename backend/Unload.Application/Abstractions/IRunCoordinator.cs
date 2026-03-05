using Unload.Core;

namespace Unload.Application;

/// <summary>
/// Активация запуска с токеном отмены конкретного run.
/// </summary>
/// <param name="Request">Запрос на выполнение.</param>
/// <param name="CancellationToken">Токен остановки конкретного запуска.</param>
public sealed record RunActivation(RunRequest Request, CancellationToken CancellationToken);

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
    /// <returns>Поток активаций запуска для background worker.</returns>
    IAsyncEnumerable<RunActivation> ReadActivationsAsync(CancellationToken cancellationToken);

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

    /// <summary>
    /// Пытается остановить активный запуск.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска для отмены.</param>
    /// <returns><c>true</c>, если отмена запрошена; иначе <c>false</c>.</returns>
    bool TryCancel(string correlationId);
}
