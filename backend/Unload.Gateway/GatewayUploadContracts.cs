namespace Unload.Gateway;

public record GatewayUploadResponse(
    string RequestId,
    string CorrelationId,
    string MemberName,
    string BatchId,
    int AcceptedFiles,
    int FailedFiles,
    IReadOnlyCollection<GatewayUploadFileResult> Files);

public record GatewayUploadFileResult(
    string FileName,
    long SizeBytes,
    string Status,
    string? Message = null);
