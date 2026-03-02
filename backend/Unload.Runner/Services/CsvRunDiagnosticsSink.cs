using System.Collections.Concurrent;
using System.Text;
using Unload.Core;

namespace Unload.Runner;

public class CsvRunDiagnosticsSink : IRunDiagnosticsSink
{
    private static readonly char[] InvalidPathChars = Path.GetInvalidFileNameChars();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _baseDirectory;

    public CsvRunDiagnosticsSink(string baseDirectory)
    {
        _baseDirectory = Path.GetFullPath(baseDirectory);
    }

    public Task WriteEventAsync(RunnerEvent @event, CancellationToken cancellationToken)
    {
        var runDirectory = GetRunDirectory(@event.CorrelationId);
        var filePath = Path.Combine(runDirectory, "events.csv");
        var row = string.Join(',',
            Csv(@event.OccurredAt.ToString("O")),
            Csv(@event.CorrelationId),
            Csv(@event.Step.ToString()),
            Csv(@event.ProfileCode),
            Csv(@event.ScriptCode),
            Csv(@event.Records?.ToString()),
            Csv(@event.FilePath),
            Csv(@event.Message));

        return AppendRowAsync(
            filePath,
            "occurred_at,correlation_id,step,profile_code,script_code,records,file_path,message",
            row,
            @event.CorrelationId,
            cancellationToken);
    }

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
            Csv(metric.ProfileCode),
            Csv(metric.ScriptCode),
            Csv(metric.Records?.ToString()),
            Csv(metric.FilePath),
            Csv(metric.Details));

        return AppendRowAsync(
            filePath,
            "occurred_at,correlation_id,step,duration_ms,outcome,profile_code,script_code,records,file_path,details",
            row,
            metric.CorrelationId,
            cancellationToken);
    }

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

    private string GetRunDirectory(string correlationId)
    {
        var safeName = correlationId;
        foreach (var invalidChar in InvalidPathChars)
        {
            safeName = safeName.Replace(invalidChar, '_');
        }

        return Path.Combine(_baseDirectory, safeName);
    }

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
