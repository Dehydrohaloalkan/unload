namespace Unload.Api;

public sealed record SenderFeedbackRequest(
    string CorrelationId,
    string MemberName,
    string BatchId,
    string Kind,
    string? FilePath,
    string? Message);
