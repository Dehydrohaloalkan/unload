using Unload.Core;
using Unload.Run.Application;
using Unload.Workflow;
using System.Text.RegularExpressions;

namespace Unload.TaskFlow;

/// <summary>
/// Definition задачи запуска основной выгрузки.
/// </summary>
public sealed class StartRunWorkflowTaskDefinition : WorkflowTaskDefinition<StartRunTaskRequest, StartRunTaskResult>
{
    private static readonly Regex TargetCodePattern = new("^[A-Z0-9_]{3,64}$", RegexOptions.Compiled);

    private readonly ICatalogService _catalogService;
    private readonly IRunRequestFactory _requestFactory;
    private readonly IRunCoordinator _runCoordinator;
    private readonly IRunStateStore _runStateStore;
    private readonly RunApplicationOptions _runOptions;
    private readonly IPresetGateService _presetGateService;
    private readonly IWorkflowTaskAccessService _taskAccessService;

    public StartRunWorkflowTaskDefinition(
        ICatalogService catalogService,
        IRunRequestFactory requestFactory,
        IRunCoordinator runCoordinator,
        IRunStateStore runStateStore,
        RunApplicationOptions runOptions,
        IPresetGateService presetGateService,
        IWorkflowTaskAccessService taskAccessService)
    {
        _catalogService = catalogService;
        _requestFactory = requestFactory;
        _runCoordinator = runCoordinator;
        _runStateStore = runStateStore;
        _runOptions = runOptions;
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
            var normalizedCodes = NormalizeTargetCodes(targetCodes);
            var outputDirectory = Path.GetFullPath(_runOptions.OutputDirectory);
            var request = _requestFactory.Create(normalizedCodes, outputDirectory);

            if (!_runCoordinator.TryActivate(request))
            {
                throw new RunAlreadyInProgressException(_runCoordinator.GetActiveCorrelationId());
            }

            try
            {
                _runStateStore.SetStarted(
                    request.CorrelationId,
                    normalizedCodes,
                    memberNames?.Where(static x => !string.IsNullOrWhiteSpace(x))
                        .Select(static x => x.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray() ?? Array.Empty<string>());
            }
            catch
            {
                _runCoordinator.Complete(request.CorrelationId);
                throw;
            }

            return request.CorrelationId;
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

    private static IReadOnlyCollection<string> NormalizeTargetCodes(IReadOnlyCollection<string> targetCodes)
    {
        var normalized = targetCodes
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
        {
            throw new InvalidOperationException("At least one target code is required.");
        }

        foreach (var code in normalized)
        {
            if (!TargetCodePattern.IsMatch(code))
            {
                throw new InvalidOperationException($"Target code '{code}' is invalid.");
            }
        }

        return normalized;
    }
}
