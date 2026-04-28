using Unload.Workflow;
using Unload.TaskFlow.Exceptions;

namespace Unload.TaskFlow;

/// <summary>
/// Definition задачи выполнения extra.
/// </summary>
public class RunExtraWorkflowTaskDefinition(
    IScriptTaskOrchestrator scriptTaskOrchestrator,
    IPresetGateService presetGateService,
    IWorkflowTaskAccessService taskAccessService,
    IWorkflowTaskTransitionService transitionService) : WorkflowTaskDefinition<EmptyWorkflowTaskRequest, ScriptTaskRunResult>
{
    private readonly IScriptTaskOrchestrator _scriptTaskOrchestrator = scriptTaskOrchestrator;
    private readonly IPresetGateService _presetGateService = presetGateService;
    private readonly IWorkflowTaskAccessService _taskAccessService = taskAccessService;
    private readonly IWorkflowTaskTransitionService _transitionService = transitionService;

    public override string TaskCode => WorkflowTaskCodes.Extra;

    protected override async Task<ScriptTaskRunResult> ExecuteTypedAsync(
        EmptyWorkflowTaskRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.AdminOverride && !_presetGateService.CanRunMainAndExtra(out var gateReason))
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
                request.AdminOverride,
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
