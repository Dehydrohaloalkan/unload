using Unload.Workflow;

namespace Unload.TaskFlow;

/// <summary>
/// Definition задачи выполнения extra.
/// </summary>
public sealed class RunExtraWorkflowTaskDefinition : WorkflowTaskDefinition<EmptyWorkflowTaskRequest, ScriptTaskRunResult>
{
    private readonly IScriptTaskOrchestrator _scriptTaskOrchestrator;
    private readonly IPresetGateService _presetGateService;
    private readonly IWorkflowTaskAccessService _taskAccessService;
    private readonly IWorkflowTaskTransitionService _transitionService;

    public RunExtraWorkflowTaskDefinition(
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

    public override string TaskCode => WorkflowTaskCodes.Extra;

    protected override async Task<ScriptTaskRunResult> ExecuteTypedAsync(
        EmptyWorkflowTaskRequest request,
        CancellationToken cancellationToken)
    {
        if (!_presetGateService.CanRunMainAndExtra(out var gateReason))
        {
            throw new WorkflowTaskDispatchException(
                WorkflowTaskFailureKind.Conflict,
                "PRESET_GATE_BLOCKED",
                gateReason);
        }

        try
        {
            var result = await _taskAccessService.ExecuteExclusiveAsync(
                WorkflowTaskCodes.Extra,
                () => _scriptTaskOrchestrator.RunExtraAsync(cancellationToken),
                markCompletedOnSuccess: true,
                cancellationToken);
            await _transitionService.HandleCompletedAsync(WorkflowTaskCodes.Extra, result, cancellationToken);
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
