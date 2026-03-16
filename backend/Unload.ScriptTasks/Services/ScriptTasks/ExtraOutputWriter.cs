using System.Collections.Concurrent;
using Unload.Core;

namespace Unload.Application;

public interface IExtraOutputWriter
{
    Task<ExtraOutputWriteResult> WriteAsync(
        string baseOutputDirectory,
        string correlationId,
        ConcurrentDictionary<string, ConcurrentQueue<string>> aggregatedLines,
        CancellationToken cancellationToken);
}

public sealed class ExtraOutputWriter : IExtraOutputWriter
{
    private readonly IScriptTaskEventPublisher _eventPublisher;

    public ExtraOutputWriter(IScriptTaskEventPublisher eventPublisher)
    {
        _eventPublisher = eventPublisher;
    }

    public async Task<ExtraOutputWriteResult> WriteAsync(
        string baseOutputDirectory,
        string correlationId,
        ConcurrentDictionary<string, ConcurrentQueue<string>> aggregatedLines,
        CancellationToken cancellationToken)
    {
        var runDirectory = CreateRunDirectory(baseOutputDirectory);
        var filesDirectory = Path.Combine(runDirectory, "output-files");
        Directory.CreateDirectory(filesDirectory);

        var filesWritten = 0;
        foreach (var item in aggregatedLines.OrderBy(static x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var bankKey = SanitizeFileNameSegment(item.Key);
            var filePath = Path.Combine(filesDirectory, $"{bankKey}.txt");
            var lines = item.Value.ToArray();
            await File.WriteAllLinesAsync(filePath, lines, cancellationToken);
            filesWritten++;

            await _eventPublisher.PublishAsync(
                correlationId,
                RunnerStep.FileWritten,
                $"Extra file written: {Path.GetFileName(filePath)}.",
                cancellationToken,
                targetCode: "EXTRA",
                records: lines.Length,
                filePath: filePath);
        }

        return new ExtraOutputWriteResult(runDirectory, filesWritten);
    }

    private static string CreateRunDirectory(string baseOutputDirectory)
    {
        Directory.CreateDirectory(baseOutputDirectory);
        var timestamp = DateTime.Now.ToString("dd_MM_yyyy_HHmmss");
        var candidate = Path.Combine(baseOutputDirectory, $"{timestamp}_extra");
        var index = 1;

        while (Directory.Exists(candidate))
        {
            candidate = Path.Combine(baseOutputDirectory, $"{timestamp}_extra_{index:D2}");
            index++;
        }

        Directory.CreateDirectory(candidate);
        return candidate;
    }

    private static string SanitizeFileNameSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "UNKNOWN";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "UNKNOWN" : sanitized;
    }
}

public sealed record ExtraOutputWriteResult(string OutputPath, int FilesWritten);
