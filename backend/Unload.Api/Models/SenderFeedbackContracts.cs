namespace Unload.Api;

public  record SenderFeedbackRequest(
    string CorrelationId,
    string MemberName,
    string BatchId,
    string Kind,
    string? FilePath,
    string? Message);
