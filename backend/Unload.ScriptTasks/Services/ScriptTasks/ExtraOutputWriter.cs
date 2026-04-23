using System.Collections.Concurrent;
using Unload.Core;

namespace Unload.ScriptTasks;

public interface IExtraOutputWriter
{
    Task<ExtraOutputWriteResult> WriteAsync(
        string baseOutputDirectory,
        string correlationId,
        ConcurrentDictionary<string, ConcurrentQueue<string>> aggregatedLines,
        CancellationToken cancellationToken);
}

public  class ExtraOutputWriter(IScriptTaskEventPublisher eventPublisher) : IExtraOutputWriter
{
    private readonly IScriptTaskEventPublisher _eventPublisher = eventPublisher;

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
        var filesByMember = new Dictionary<string, List<SenderFileDescriptor>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in aggregatedLines.OrderBy(static x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var bankKey = SanitizeFileNameSegment(item.Key);
            var filePath = Path.Combine(filesDirectory, $"{bankKey}.txt");
            var lines = item.Value.ToArray();
            await File.WriteAllLinesAsync(filePath, lines, cancellationToken);
            filesWritten++;

            var fileInfo = new FileInfo(filePath);
            filesByMember[bankKey] =
            [
                new SenderFileDescriptor(
                    FilePath: filePath,
                    FileName: fileInfo.Name,
                    SizeBytes: fileInfo.Length)
            ];
        }

        foreach (var memberBatch in filesByMember)
        {
            await _eventPublisher.PublishFileBatchReadyAsync(
                correlationId,
                memberBatch.Key,
                memberBatch.Value,
                cancellationToken);
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

public  record ExtraOutputWriteResult(string OutputPath, int FilesWritten);
