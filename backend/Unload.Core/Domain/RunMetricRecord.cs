namespace Unload.Core;

public record RunMetricRecord(
    DateTimeOffset OccurredAt,
    string CorrelationId,
    RunnerStep Step,
    long DurationMs,
    string Outcome,
    string? ProfileCode = null,
    string? ScriptCode = null,
    int? Records = null,
    string? FilePath = null,
    string? Details = null);
