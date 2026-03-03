namespace Unload.Core;

/// <summary>
/// Контракт записи диагностических данных запуска.
/// Используется раннером для логирования событий и метрик производительности.
/// </summary>
public interface IRunDiagnosticsSink
{
    /// <summary>
    /// Сохраняет событие выполнения запуска.
    /// </summary>
    /// <param name="event">Событие раннера.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Задача завершения записи события.</returns>
    Task WriteEventAsync(RunnerEvent @event, CancellationToken cancellationToken);

    /// <summary>
    /// Сохраняет метрику длительности шага запуска.
    /// </summary>
    /// <param name="metric">Метрика выполнения шага.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Задача завершения записи метрики.</returns>
    Task WriteMetricAsync(RunMetricRecord metric, CancellationToken cancellationToken);
}
