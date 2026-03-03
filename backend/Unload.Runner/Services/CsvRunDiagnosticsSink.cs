using System.Collections.Concurrent;
using System.Text;
using Unload.Core;

namespace Unload.Runner;

/// <summary>
/// Диагностический sink, сохраняющий события и метрики запуска в CSV-файлы.
/// Используется раннером для пост-анализа выполнения и наблюдаемости.
/// </summary>
public class CsvRunDiagnosticsSink : IRunDiagnosticsSink
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _baseDirectory;

    /// <summary>
    /// Создает sink и нормализует базовую директорию диагностики.
    /// </summary>
    /// <param name="baseDirectory">Корневая директория, где создаются папки запусков.</param>
    public CsvRunDiagnosticsSink(string baseDirectory)
    {
        _baseDirectory = Path.GetFullPath(baseDirectory);
    }

    /// <summary>
    /// Записывает событие выполнения в <c>events.csv</c> соответствующего запуска.
    /// </summary>
    /// <param name="event">Событие раннера для записи.</param>
    /// <param name="cancellationToken">Токен отмены записи.</param>
    /// <returns>Задача завершения записи.</returns>
    public Task WriteEventAsync(RunnerEvent @event, CancellationToken cancellationToken)
    {
        var runDirectory = DiagnosticsRunPathHelper.GetRunDirectory(_baseDirectory, @event.CorrelationId);
        var filePath = Path.Combine(runDirectory, "events.csv");
        var row = string.Join(',',
            CsvValueEscaper.Csv(@event.OccurredAt.ToString("O")),
            CsvValueEscaper.Csv(@event.CorrelationId),
            CsvValueEscaper.Csv(@event.Step.ToString()),
            CsvValueEscaper.Csv(@event.TargetCode),
            CsvValueEscaper.Csv(@event.ScriptCode),
            CsvValueEscaper.Csv(@event.Records?.ToString()),
            CsvValueEscaper.Csv(@event.FilePath),
            CsvValueEscaper.Csv(@event.Message));

        return AppendRowAsync(
            filePath,
            "occurred_at,correlation_id,step,target_code,script_code,records,file_path,message",
            row,
            @event.CorrelationId,
            cancellationToken);
    }

    /// <summary>
    /// Записывает метрику выполнения в <c>metrics.csv</c> соответствующего запуска.
    /// </summary>
    /// <param name="metric">Метрика длительности шага.</param>
    /// <param name="cancellationToken">Токен отмены записи.</param>
    /// <returns>Задача завершения записи.</returns>
    public Task WriteMetricAsync(RunMetricRecord metric, CancellationToken cancellationToken)
    {
        var runDirectory = DiagnosticsRunPathHelper.GetRunDirectory(_baseDirectory, metric.CorrelationId);
        var filePath = Path.Combine(runDirectory, "metrics.csv");
        var row = string.Join(',',
            CsvValueEscaper.Csv(metric.OccurredAt.ToString("O")),
            CsvValueEscaper.Csv(metric.CorrelationId),
            CsvValueEscaper.Csv(metric.Step.ToString()),
            CsvValueEscaper.Csv(metric.DurationMs.ToString()),
            CsvValueEscaper.Csv(metric.Outcome),
            CsvValueEscaper.Csv(metric.TargetCode),
            CsvValueEscaper.Csv(metric.ScriptCode),
            CsvValueEscaper.Csv(metric.Records?.ToString()),
            CsvValueEscaper.Csv(metric.FilePath),
            CsvValueEscaper.Csv(metric.Details));

        return AppendRowAsync(
            filePath,
            "occurred_at,correlation_id,step,duration_ms,outcome,target_code,script_code,records,file_path,details",
            row,
            metric.CorrelationId,
            cancellationToken);
    }

    /// <summary>
    /// Добавляет строку в CSV-файл, создавая заголовок при первом обращении.
    /// Выполняет запись под локом на конкретный <c>correlationId</c>.
    /// </summary>
    /// <param name="filePath">Путь к CSV-файлу назначения.</param>
    /// <param name="header">Строка заголовка CSV.</param>
    /// <param name="row">Строка данных CSV.</param>
    /// <param name="correlationId">Идентификатор запуска для выбора синхронизационного лока.</param>
    /// <param name="cancellationToken">Токен отмены записи.</param>
    private async Task AppendRowAsync(
        string filePath,
        string header,
        string row,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var semaphore = Locks.GetOrAdd(correlationId, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(filePath)
                ?? throw new InvalidOperationException("CSV target directory is invalid.");
            Directory.CreateDirectory(directory);

            if (!File.Exists(filePath))
            {
                await File.WriteAllTextAsync(filePath, header + Environment.NewLine, Encoding.UTF8, cancellationToken);
            }

            await File.AppendAllTextAsync(filePath, row + Environment.NewLine, Encoding.UTF8, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

}
