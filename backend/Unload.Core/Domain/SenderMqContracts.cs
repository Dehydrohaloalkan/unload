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
    BatchFailed
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
    string? Message = null);
