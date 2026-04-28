using Unload.Run.Application;

namespace Unload.Api.Models;

public sealed record WorkflowHistoryResponse(
    DateOnly FromDayInclusive,
    DateOnly ToDayInclusive,
    int Days,
    IReadOnlyList<RunStatusInfo> Runs,
    IReadOnlyList<TaskRecord> TaskHistory);

