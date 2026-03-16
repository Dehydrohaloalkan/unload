using Unload.Core;
using Unload.Workflow;

namespace Unload.Application;

/// <summary>
/// Definition задачи запуска основной выгрузки.
/// </summary>
public sealed class StartRunWorkflowTaskDefinition : WorkflowTaskDefinition<StartRunTaskRequest, StartRunTaskResult>
{
    private readonly ICatalogService _catalogService;
    private readonly IRunOrchestrator _orchestrator;
    private readonly IPresetGateService _presetGateService;
    private readonly IWorkflowTaskAccessService _taskAccessService;

    public StartRunWorkflowTaskDefinition(
        ICatalogService catalogService,
        IRunOrchestrator orchestrator,
        IPresetGateService presetGateService,
        IWorkflowTaskAccessService taskAccessService)
    {
        _catalogService = catalogService;
        _orchestrator = orchestrator;
        _presetGateService = presetGateService;
        _taskAccessService = taskAccessService;
    }

    public override string TaskCode => WorkflowTaskCodes.Run;

    protected override async Task<StartRunTaskResult> ExecuteTypedAsync(
        StartRunTaskRequest request,
        CancellationToken cancellationToken)
    {
        if (!_presetGateService.CanRunMainAndExtra(out var gateReason))
        {
            throw new WorkflowTaskDispatchException(
                WorkflowTaskFailureKind.Conflict,
                "PRESET_GATE_BLOCKED",
                gateReason);
        }

        if (request.Codes is null)
        {
            throw new WorkflowTaskDispatchException(
                WorkflowTaskFailureKind.Validation,
                "VALIDATION_ERROR",
                "Codes payload is required.");
        }

        var normalizedCodes = request.Codes
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedCodes.Length == 0)
        {
            throw new WorkflowTaskDispatchException(
                WorkflowTaskFailureKind.Validation,
                "VALIDATION_ERROR",
                "At least one code is required.");
        }

        var correlationId = request.SelectionMode switch
        {
            RunSelectionMode.MemberCodes => await StartByMemberCodesAsync(normalizedCodes, cancellationToken),
            RunSelectionMode.TargetCodes => StartByTargetCodes(normalizedCodes),
            _ => throw new WorkflowTaskDispatchException(
                WorkflowTaskFailureKind.Validation,
                "VALIDATION_ERROR",
                $"Unsupported run selection mode '{request.SelectionMode}'.")
        };

        return new StartRunTaskResult(correlationId);
    }

    private async Task<string> StartByMemberCodesAsync(
        IReadOnlyCollection<string> memberCodes,
        CancellationToken cancellationToken)
    {
        var catalog = await _catalogService.GetCatalogAsync(cancellationToken);
        var selectedMembers = catalog.Members
            .Where(member => memberCodes.Contains(member.Code, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var selectedCodes = selectedMembers
            .Select(static x => x.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownCodes = memberCodes.Where(code => !selectedCodes.Contains(code)).ToArray();
        if (unknownCodes.Length > 0)
        {
            throw new WorkflowTaskDispatchException(
                WorkflowTaskFailureKind.Validation,
                "UNKNOWN_MEMBER_CODES",
                $"Unknown member codes: {string.Join(", ", unknownCodes)}");
        }

        var selectedMemberIds = selectedMembers.Select(static x => x.Id).ToHashSet();
        var targetCodes = catalog.Targets
            .Where(target => selectedMemberIds.Contains(target.MemberId))
            .Select(static target => target.TargetCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (targetCodes.Length == 0)
        {
            throw new WorkflowTaskDispatchException(
                WorkflowTaskFailureKind.Validation,
                "TARGET_CODES_NOT_FOUND",
                "No target codes found for selected members.");
        }

        return _taskAccessService.ExecuteDeferredStart(
            WorkflowTaskCodes.Run,
            () => StartRunCore(targetCodes, selectedMembers.Select(static x => x.Name).ToArray()));
    }

    private string StartByTargetCodes(IReadOnlyCollection<string> targetCodes)
    {
        return _taskAccessService.ExecuteDeferredStart(
            WorkflowTaskCodes.Run,
            () => StartRunCore(targetCodes, memberNames: null));
    }

    private string StartRunCore(IReadOnlyCollection<string> targetCodes, IReadOnlyCollection<string>? memberNames)
    {
        try
        {
            return _orchestrator.StartRun(targetCodes, memberNames);
        }
        catch (RunAlreadyInProgressException ex)
        {
            throw new WorkflowTaskDispatchException(
                WorkflowTaskFailureKind.Conflict,
                "RUN_ALREADY_IN_PROGRESS",
                ex.Message,
                ex.ActiveCorrelationId is null
                    ? null
                    : new Dictionary<string, object?> { ["activeCorrelationId"] = ex.ActiveCorrelationId });
        }
    }
}
