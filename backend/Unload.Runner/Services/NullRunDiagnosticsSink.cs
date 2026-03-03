using Unload.Core;

namespace Unload.Runner;

/// <summary>
/// Пустая реализация диагностики, которая игнорирует все события и метрики.
/// Используется в сценариях, где запись диагностики не требуется.
/// </summary>
public class NullRunDiagnosticsSink : IRunDiagnosticsSink
{
    /// <summary>
    /// Игнорирует событие выполнения.
    /// </summary>
    /// <param name="event">Событие раннера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Уже завершенная задача.</returns>
    public Task WriteEventAsync(RunnerEvent @event, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Игнорирует метрику выполнения.
    /// </summary>
    /// <param name="metric">Метрика этапа.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Уже завершенная задача.</returns>
    public Task WriteMetricAsync(RunMetricRecord metric, CancellationToken cancellationToken) => Task.CompletedTask;
}
