namespace Unload.Api.Models;

public record SenderFeedbackRequest(
    string CorrelationId,
    string MemberName,
    string BatchId,
    string Kind,
    string? FilePath,
    string? Message);
