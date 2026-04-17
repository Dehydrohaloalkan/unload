using Unload.TaskFlow;

namespace Unload.Api;

public sealed record WorkflowDashboardSnapshotResponse(
    PresetGateState PresetState,
    bool HasRunToday,
    bool HasExtraToday,
    DateTimeOffset? RunLastCompletedAt,
    DateTimeOffset? ExtraLastCompletedAt,
    IReadOnlyCollection<TaskRecord> TodayHistory);
