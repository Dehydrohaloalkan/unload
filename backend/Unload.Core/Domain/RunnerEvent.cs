namespace Unload.Core;

public record RunnerEvent(
    DateTimeOffset OccurredAt,
    string CorrelationId,
    RunnerStep Step,
    string Message,
    string? ProfileCode = null,
    string? ScriptCode = null,
    int? Records = null,
    string? FilePath = null);
