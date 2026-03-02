using Unload.Core;

namespace Unload.Application;

public enum RunLifecycleStatus
{
    Queued,
    Running,
    Completed,
    Failed
}

public record RunStatusInfo(
    string CorrelationId,
    RunLifecycleStatus Status,
    IReadOnlyCollection<string> ProfileCodes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    RunnerStep? LastStep = null,
    string? Message = null,
    string? OutputPath = null);
