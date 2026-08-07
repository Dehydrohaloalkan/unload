using Unload.Core;

namespace Unload.Store;

/// <summary>
/// Чисто проецирует одно sender feedback событие в карту состояний gateway batches.
/// </summary>
internal static class GatewayFeedbackProjector
{
    public static IReadOnlyDictionary<string, SenderBatchStatusInfo> Apply(
        IReadOnlyDictionary<string, SenderBatchStatusInfo>? source,
        SenderFileDispatchFeedback feedback,
        DateTimeOffset now)
    {
        var map = source is null
            ? new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, SenderBatchStatusInfo>(source, StringComparer.OrdinalIgnoreCase);

        map.TryGetValue(feedback.BatchId, out var currentBatch);
        var sentFiles = currentBatch?.SentFiles?.ToList() ?? [];

        if (feedback.Kind == SenderFeedbackKind.FileSent && !string.IsNullOrWhiteSpace(feedback.FilePath))
        {
            var normalizedPath = NormalizePathSafe(feedback.FilePath);
            if (sentFiles.All(file =>
                    !string.Equals(file.FilePath, normalizedPath, StringComparison.OrdinalIgnoreCase)))
            {
                sentFiles.Add(new SenderFileDispatchStateInfo(normalizedPath, feedback.OccurredAt));
            }
        }

        var status = feedback.Kind switch
        {
            SenderFeedbackKind.FileSent => SenderBatchStatus.InProgress,
            SenderFeedbackKind.BatchCompleted => SenderBatchStatus.Completed,
            SenderFeedbackKind.BatchFailed => SenderBatchStatus.Failed,
            _ => SenderBatchStatus.InProgress
        };

        map[feedback.BatchId] = new SenderBatchStatusInfo(
            BatchId: feedback.BatchId,
            MemberName: feedback.MemberName,
            Status: status,
            UpdatedAt: now,
            SentFiles: sentFiles
                .OrderBy(static file => file.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Message: feedback.Message);

        return map;
    }

    private static string NormalizePathSafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch
        {
            return path.Trim();
        }
    }
}
