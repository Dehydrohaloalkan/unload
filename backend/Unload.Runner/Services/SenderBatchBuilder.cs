using System.Collections.Concurrent;
using Unload.Core;

namespace Unload.Runner;

internal sealed class SenderBatchBuilder
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<SenderFileDescriptor>> _filesByMember =
        new(StringComparer.OrdinalIgnoreCase);

    public void Add(string memberName, string filePath)
    {
        if (string.IsNullOrWhiteSpace(memberName) || string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var normalizedPath = Path.GetFullPath(filePath);
        var fileInfo = new FileInfo(normalizedPath);
        if (!fileInfo.Exists)
        {
            return;
        }

        var descriptor = new SenderFileDescriptor(
            FilePath: normalizedPath,
            FileName: fileInfo.Name,
            SizeBytes: fileInfo.Length);

        var memberFiles = _filesByMember.GetOrAdd(memberName.Trim(), _ => new ConcurrentBag<SenderFileDescriptor>());
        memberFiles.Add(descriptor);
    }

    public IReadOnlyCollection<SenderFileBatchReadyEvent> BuildBatchEvents(string correlationId)
    {
        return _filesByMember
            .Where(static pair => pair.Value.Count > 0)
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new SenderFileBatchReadyEvent(
                OccurredAt: DateTimeOffset.UtcNow,
                CorrelationId: correlationId,
                MemberName: pair.Key,
                BatchId: $"{correlationId}:{pair.Key}",
                Version: 1,
                Files: pair.Value
                    .DistinctBy(static file => file.FilePath, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static file => file.FileName, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .ToArray();
    }
}
