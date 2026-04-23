using Unload.TaskFlow;

namespace Unload.Api;

public  record WorkflowDashboardSnapshotResponse(
    PresetGateState PresetState,
    bool HasRunToday,
    bool HasExtraToday,
    DateTimeOffset? RunLastCompletedAt,
    DateTimeOffset? ExtraLastCompletedAt,
    IReadOnlyCollection<TaskRecord> TodayHistory);
