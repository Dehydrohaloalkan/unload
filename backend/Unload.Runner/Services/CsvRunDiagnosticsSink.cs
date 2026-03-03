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
    private static readonly char[] InvalidPathChars = Path.GetInvalidFileNameChars();
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
        var runDirectory = GetRunDirectory(@event.CorrelationId);
        var filePath = Path.Combine(runDirectory, "events.csv");
        var row = string.Join(',',
            Csv(@event.OccurredAt.ToString("O")),
            Csv(@event.CorrelationId),
            Csv(@event.Step.ToString()),
            Csv(@event.TargetCode),
            Csv(@event.ScriptCode),
            Csv(@event.Records?.ToString()),
            Csv(@event.FilePath),
            Csv(@event.Message));

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
        var runDirectory = GetRunDirectory(metric.CorrelationId);
        var filePath = Path.Combine(runDirectory, "metrics.csv");
        var row = string.Join(',',
            Csv(metric.OccurredAt.ToString("O")),
            Csv(metric.CorrelationId),
            Csv(metric.Step.ToString()),
            Csv(metric.DurationMs.ToString()),
            Csv(metric.Outcome),
            Csv(metric.TargetCode),
            Csv(metric.ScriptCode),
            Csv(metric.Records?.ToString()),
            Csv(metric.FilePath),
            Csv(metric.Details));

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

    /// <summary>
    /// Возвращает директорию запуска с безопасным именем на основе correlation id.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <returns>Путь к директории диагностических файлов запуска.</returns>
    private string GetRunDirectory(string correlationId)
    {
        var safeName = correlationId;
        foreach (var invalidChar in InvalidPathChars)
        {
            safeName = safeName.Replace(invalidChar, '_');
        }

        return Path.Combine(_baseDirectory, safeName);
    }

    /// <summary>
    /// Экранирует значение для безопасной записи в CSV.
    /// Дополнительно защищает от formula-injection в табличных редакторах.
    /// </summary>
    /// <param name="value">Исходное строковое значение.</param>
    /// <returns>Экранированное значение в CSV-формате.</returns>
    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var safe = value;
        if (safe.Length > 0 && "=+-@".Contains(safe[0], StringComparison.Ordinal))
        {
            safe = $"'{safe}";
        }

        safe = safe.Replace("\"", "\"\"");
        return $"\"{safe}\"";
    }
}
