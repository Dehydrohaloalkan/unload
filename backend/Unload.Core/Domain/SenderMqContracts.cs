namespace Unload.Core;

public enum SenderBatchStatus
{
    Ready,
    InProgress,
    Completed,
    Failed
}

public enum SenderFeedbackKind
{
    FileSent,
    BatchCompleted,
    BatchFailed
}

public sealed record SenderFileDescriptor(
    string FilePath,
    string FileName,
    long SizeBytes,
    string? Checksum = null);

public sealed record SenderFileBatchReadyEvent(
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string MemberName,
    string BatchId,
    int Version,
    IReadOnlyCollection<SenderFileDescriptor> Files);

public sealed record SenderFileDispatchFeedback(
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string MemberName,
    string BatchId,
    SenderFeedbackKind Kind,
    string? FilePath = null,
    string? Message = null);
