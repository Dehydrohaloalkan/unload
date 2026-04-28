using Unload.Api.Abstractions;
using Unload.TaskFlow;

namespace Unload.Api.Services;

public sealed class WorkflowInMemoryStateRestorer(
    IWorkflowTaskAccessService workflowTaskAccessService,
    IWorkflowStageStateStore workflowStageStateStore,
    IPresetGateService presetGateService,
    ITaskExecutionHistoryStore taskExecutionHistoryStore,
    ILogger<WorkflowInMemoryStateRestorer> logger) : IWorkflowInMemoryStateRestorer
{
    private readonly IWorkflowTaskAccessService _workflowTaskAccessService = workflowTaskAccessService;
    private readonly IWorkflowStageStateStore _workflowStageStateStore = workflowStageStateStore;
    private readonly IPresetGateService _presetGateService = presetGateService;
    private readonly ITaskExecutionHistoryStore _taskExecutionHistoryStore = taskExecutionHistoryStore;
    private readonly ILogger<WorkflowInMemoryStateRestorer> _logger = logger;

    public void RestoreForToday()
    {
        // These services are in-memory, but the fact of completion for the day is persisted in history.
        // Restore it early so API requests observe consistent dependency state after restart.
        try
        {
            _workflowTaskAccessService.ResetCompletedTasks();
            _workflowStageStateStore.Reset();

            var today = DateOnly.FromDateTime(DateTime.Now);

            if (_taskExecutionHistoryStore.HasRunToday(WorkflowTaskCodes.Preset, today))
            {
                _workflowTaskAccessService.MarkCompleted(WorkflowTaskCodes.Preset);
                _presetGateService.MarkPresetCompleted();

                // If preset is completed, the probe-ready stage is implicitly satisfied for today.
                _workflowStageStateStore.MarkCompleted(WorkflowStageCodes.PresetProbeReady);
            }

            if (_taskExecutionHistoryStore.HasRunToday(WorkflowTaskCodes.Extra, today))
            {
                _workflowTaskAccessService.MarkCompleted(WorkflowTaskCodes.Extra);
            }

            if (_taskExecutionHistoryStore.HasRunToday(WorkflowTaskCodes.Run, today))
            {
                _workflowTaskAccessService.MarkCompleted(WorkflowTaskCodes.Run);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore in-memory workflow state from history.");
        }
    }
}

