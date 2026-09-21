using Unload.Core;

namespace Unload.Store;

/// <summary>
/// Чисто проецирует одно sender feedback событие в карту состояний gateway batches.
/// </summary>
internal static class GatewayFeedbackProjector
{
    public static IReadOnlyDictionary<string, SenderBatchStatusInfo> ApplyQueued(
        IReadOnlyDictionary<string, SenderBatchStatusInfo>? source,
        RunnerEvent @event,
        DateTimeOffset now)
    {
        if (@event.Step != RunnerStep.GatewayBatchQueued || string.IsNullOrWhiteSpace(@event.BatchId))
        {
            return source ?? new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase);
        }

        var map = source is null
            ? new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, SenderBatchStatusInfo>(source, StringComparer.OrdinalIgnoreCase);

        map.TryGetValue(@event.BatchId, out var currentBatch);
        map[@event.BatchId] = new SenderBatchStatusInfo(
            BatchId: @event.BatchId,
            MemberName: FirstNonEmpty(currentBatch?.MemberName, @event.MemberName),
            Status: currentBatch?.Status ?? SenderBatchStatus.Ready,
            UpdatedAt: currentBatch?.UpdatedAt ?? now,
            SentFiles: currentBatch?.SentFiles ?? Array.Empty<SenderFileDispatchStateInfo>(),
            Message: currentBatch?.Message ?? @event.Message,
            QueuedAt: currentBatch?.QueuedAt ?? @event.OccurredAt,
            StartedAt: currentBatch?.StartedAt,
            FileCount: @event.BatchFileCount ?? currentBatch?.FileCount);

        return map;
    }

    public static IReadOnlyDictionary<string, SenderBatchStatusInfo> Apply(
        IReadOnlyDictionary<string, SenderBatchStatusInfo>? source,
        SenderFileDispatchFeedback feedback,
        DateTimeOffset now)
    {
        var map = source is null
            ? new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, SenderBatchStatusInfo>(source, StringComparer.OrdinalIgnoreCase);

        map.TryGetValue(feedback.BatchId, out var currentBatch);
        if (IsTerminal(currentBatch?.Status))
        {
            return map;
        }

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

        var requestedStatus = feedback.Kind switch
        {
            SenderFeedbackKind.BatchStarted => SenderBatchStatus.InProgress,
            SenderFeedbackKind.FileSent => SenderBatchStatus.InProgress,
            SenderFeedbackKind.BatchCompleted => SenderBatchStatus.Completed,
            SenderFeedbackKind.BatchFailed => SenderBatchStatus.Failed,
            _ => SenderBatchStatus.InProgress
        };
        map[feedback.BatchId] = new SenderBatchStatusInfo(
            BatchId: feedback.BatchId,
            MemberName: FirstNonEmpty(feedback.MemberName, currentBatch?.MemberName),
            Status: requestedStatus,
            UpdatedAt: now,
            SentFiles: sentFiles
                .OrderBy(static file => file.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Message: feedback.Message ?? currentBatch?.Message,
            QueuedAt: currentBatch?.QueuedAt,
            StartedAt: currentBatch?.StartedAt ??
                (feedback.Kind is SenderFeedbackKind.BatchStarted or SenderFeedbackKind.FileSent
                    ? feedback.OccurredAt
                    : null),
            FileCount: currentBatch?.FileCount);

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

    private static bool IsTerminal(SenderBatchStatus? status) =>
        status is SenderBatchStatus.Completed or SenderBatchStatus.Failed;

    private static string FirstNonEmpty(string? primary, string? fallback) =>
        !string.IsNullOrWhiteSpace(primary) ? primary : fallback ?? string.Empty;
}
