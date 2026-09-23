namespace Unload.Core;

public enum SenderBatchStatus
{
    Ready,
    InProgress,
    Completed,
    Failed,
    SkippedByRequest
}

public enum SenderFeedbackKind
{
    FileSent,
    BatchCompleted,
    BatchFailed,
    BatchStarted
}

public record SenderFileDescriptor(
    string FilePath,
    string FileName,
    long SizeBytes,
    string? Checksum = null);

public record SenderFileBatchReadyEvent(
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string MemberName,
    string BatchId,
    int Version,
    IReadOnlyCollection<SenderFileDescriptor> Files);

public record SenderFileDispatchFeedback(
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string MemberName,
    string BatchId,
    SenderFeedbackKind Kind,
    string? FilePath = null,
    string? Message = null,
    RunnerFailureInfo? Failure = null);

/// <summary>
/// Public projection of a file that belongs to a gateway batch.
/// The descriptor is emitted with the queued runner event and enriched with
/// <see cref="SentAt"/> when matching sender feedback arrives.
/// </summary>
public record SenderBatchFileStatusInfo(
    string FilePath,
    string FileName,
    long? EstimatedBytes,
    long? ActualBytes,
    DateTimeOffset QueuedAt,
    DateTimeOffset? SentAt = null);
