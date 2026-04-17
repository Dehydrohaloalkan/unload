using Unload.Workflow;

namespace Unload.TaskFlow;

/// <summary>
/// Definition задачи выполнения preset.
/// </summary>
public sealed class RunPresetWorkflowTaskDefinition : WorkflowTaskDefinition<EmptyWorkflowTaskRequest, ScriptTaskRunResult>
{
    private readonly IScriptTaskOrchestrator _scriptTaskOrchestrator;
    private readonly IPresetGateService _presetGateService;
    private readonly IWorkflowTaskAccessService _taskAccessService;
    private readonly IWorkflowTaskTransitionService _transitionService;

    public RunPresetWorkflowTaskDefinition(
        IScriptTaskOrchestrator scriptTaskOrchestrator,
        IPresetGateService presetGateService,
        IWorkflowTaskAccessService taskAccessService,
        IWorkflowTaskTransitionService transitionService)
    {
        _scriptTaskOrchestrator = scriptTaskOrchestrator;
        _presetGateService = presetGateService;
        _taskAccessService = taskAccessService;
        _transitionService = transitionService;
    }

    public override string TaskCode => WorkflowTaskCodes.Preset;

    protected override async Task<ScriptTaskRunResult> ExecuteTypedAsync(
        EmptyWorkflowTaskRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.AdminOverride && !_presetGateService.CanRunPreset(out var reason))
        {
            throw new WorkflowTaskDispatchException(
                WorkflowTaskFailureKind.Conflict,
                "PRESET_GATE_BLOCKED",
                reason);
        }

        try
        {
            var result = await _taskAccessService.ExecuteExclusiveAsync(
                WorkflowTaskCodes.Preset,
                () => _scriptTaskOrchestrator.RunPresetAsync(cancellationToken),
                markCompletedOnSuccess: true,
                request.AdminOverride,
                cancellationToken);
            _presetGateService.MarkPresetCompleted();
            await _transitionService.HandleCompletedAsync(WorkflowTaskCodes.Preset, result, cancellationToken);
            return result;
        }
        catch (InvalidOperationException ex)
        {
            throw new WorkflowTaskDispatchException(
                WorkflowTaskFailureKind.Conflict,
                "SCRIPT_TASK_CONFLICT",
                ex.Message);
        }
    }
}
