namespace Unload.Api.Models;

public record MqUploadResponse(
    string RequestId,
    string CorrelationId,
    string MemberName,
    string BatchId,
    int AcceptedFiles,
    int FailedFiles,
    IReadOnlyCollection<MqUploadFileResult> Files);

public record MqUploadFileResult(
    string FileName,
    long SizeBytes,
    string Status,
    string? Message = null);

