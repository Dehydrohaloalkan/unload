namespace Unload.Api.Models;

public record TaskRecord(
    string TaskCode,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string? CorrelationId,
    string? Message,
    int? ScriptsExecuted,
    int? FilesWritten,
    string? OutputPath);

