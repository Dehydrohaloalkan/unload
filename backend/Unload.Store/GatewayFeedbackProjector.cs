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
        var plannedFiles = MergeQueuedFiles(currentBatch, @event);
        map[@event.BatchId] = new SenderBatchStatusInfo(
            BatchId: @event.BatchId,
            MemberName: FirstNonEmpty(@event.MemberName, currentBatch?.MemberName),
            Status: currentBatch?.Status ?? SenderBatchStatus.Ready,
            UpdatedAt: currentBatch?.UpdatedAt ?? now,
            SentFiles: currentBatch?.SentFiles ?? Array.Empty<SenderFileDispatchStateInfo>(),
            Message: currentBatch?.Message ?? @event.Message,
            QueuedAt: currentBatch?.QueuedAt ?? @event.OccurredAt,
            StartedAt: currentBatch?.StartedAt,
            FileCount: @event.BatchFileCount ?? currentBatch?.FileCount,
            Sequence: @event.Sequence > 0 ? @event.Sequence : currentBatch?.Sequence,
            Failure: currentBatch?.Failure,
            PlannedFiles: plannedFiles);

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
        var failure = feedback.Failure is null
            ? currentBatch?.Failure
            : RunnerFailureMessages.Sanitize(feedback.Failure);
        if (IsTerminal(currentBatch?.Status))
        {
            return map;
        }

        var sentFiles = currentBatch?.SentFiles?.ToList() ?? [];
        var plannedFiles = currentBatch?.PlannedFiles?.ToArray();

        if (feedback.Kind == SenderFeedbackKind.FileSent && !string.IsNullOrWhiteSpace(feedback.FilePath))
        {
            var normalizedPath = NormalizePathSafe(feedback.FilePath);
            if (sentFiles.All(file =>
                    !string.Equals(
                        NormalizePathSafe(file.FilePath),
                        normalizedPath,
                        StringComparison.OrdinalIgnoreCase)))
            {
                sentFiles.Add(new SenderFileDispatchStateInfo(normalizedPath, feedback.OccurredAt));
            }

            if (plannedFiles is not null)
            {
                plannedFiles = plannedFiles
                    .Select(file => string.Equals(
                            NormalizePathSafe(file.FilePath),
                            normalizedPath,
                            StringComparison.OrdinalIgnoreCase)
                        ? file with { SentAt = file.SentAt ?? feedback.OccurredAt }
                        : file)
                    .ToArray();
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
            MemberName: FirstNonEmpty(currentBatch?.MemberName, feedback.MemberName),
            Status: requestedStatus,
            UpdatedAt: now,
            SentFiles: sentFiles
                .OrderBy(static file => file.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Message: feedback.Kind == SenderFeedbackKind.BatchFailed
                ? RunnerFailureMessages.ForStage("sender")
                : feedback.Message ?? currentBatch?.Message,
            QueuedAt: currentBatch?.QueuedAt,
            StartedAt: currentBatch?.StartedAt ??
                (feedback.Kind is SenderFeedbackKind.BatchStarted or SenderFeedbackKind.FileSent
                    ? feedback.OccurredAt
                    : null),
            FileCount: currentBatch?.FileCount,
            Sequence: currentBatch?.Sequence,
            Failure: failure,
            PlannedFiles: plannedFiles);

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

    private static IReadOnlyCollection<SenderBatchFileStatusInfo>? MergeQueuedFiles(
        SenderBatchStatusInfo? currentBatch,
        RunnerEvent @event)
    {
        if (@event.BatchFiles is null)
        {
            return currentBatch?.PlannedFiles;
        }

        var existingPlanned = (currentBatch?.PlannedFiles ?? Array.Empty<SenderBatchFileStatusInfo>())
            .GroupBy(file => NormalizePathSafe(file.FilePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var sentFiles = (currentBatch?.SentFiles ?? Array.Empty<SenderFileDispatchStateInfo>())
            .GroupBy(file => NormalizePathSafe(file.FilePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Min(file => file.SentAt), StringComparer.OrdinalIgnoreCase);

        return @event.BatchFiles
            .Where(static file => !string.IsNullOrWhiteSpace(file.FilePath))
            .Select(file =>
            {
                var path = NormalizePathSafe(file.FilePath);
                existingPlanned.TryGetValue(path, out var existing);
                var feedbackSentAt = sentFiles.TryGetValue(path, out var sentAt)
                    ? sentAt
                    : (DateTimeOffset?)null;
                return file with
                {
                    FilePath = path,
                    FileName = FirstNonEmpty(file.FileName, Path.GetFileName(path)),
                    SentAt = file.SentAt ?? existing?.SentAt ?? feedbackSentAt
                };
            })
            .DistinctBy(static file => file.FilePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static file => file.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsTerminal(SenderBatchStatus? status) =>
        status is SenderBatchStatus.Completed or SenderBatchStatus.Failed;

    private static string FirstNonEmpty(string? primary, string? fallback) =>
        !string.IsNullOrWhiteSpace(primary) ? primary : fallback ?? string.Empty;
}
