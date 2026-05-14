using Unload.Core;
using Unload.Run.Application;
using Unload.TaskFlow.Exceptions;
using Unload.Workflow;
using System.Text.RegularExpressions;

namespace Unload.TaskFlow;

/// <summary>
/// Definition задачи запуска основной выгрузки.
/// </summary>
public class StartRunWorkflowTaskDefinition(
    ICatalogService catalogService,
    IRunRequestFactory requestFactory,
    ISingleActiveWorkflow<RunRequest> runWorkflow,
    IRunStateStore runStateStore,
    RunApplicationOptions runOptions,
    IPresetGateService presetGateService,
    IWorkflowTaskAccessService taskAccessService) : WorkflowTaskDefinition<StartRunTaskRequest, StartRunTaskResult>
{
    private static readonly Regex TargetCodePattern = new("^[A-Z0-9_]{3,64}$", RegexOptions.Compiled);

    private readonly ICatalogService _catalogService = catalogService;
    private readonly IRunRequestFactory _requestFactory = requestFactory;
    private readonly ISingleActiveWorkflow<RunRequest> _runWorkflow = runWorkflow;
    private readonly IRunStateStore _runStateStore = runStateStore;
    private readonly RunApplicationOptions _runOptions = runOptions;
    private readonly IPresetGateService _presetGateService = presetGateService;
    private readonly IWorkflowTaskAccessService _taskAccessService = taskAccessService;

    public override string TaskCode => WorkflowTaskCodes.Run;

    protected override async Task<StartRunTaskResult> ExecuteTypedAsync(
        StartRunTaskRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.AdminOverride && !_presetGateService.CanRunMainAndExtra(out var gateReason))
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
            RunSelectionMode.MemberCodes => await StartByMemberCodesAsync(normalizedCodes, request.AdminOverride, request.PublishToGateway, cancellationToken),
            RunSelectionMode.TargetCodes => StartByTargetCodes(normalizedCodes, request.AdminOverride, request.PublishToGateway),
            _ => throw new WorkflowTaskDispatchException(
                WorkflowTaskFailureKind.Validation,
                "VALIDATION_ERROR",
                $"Unsupported run selection mode '{request.SelectionMode}'.")
        };

        return new StartRunTaskResult(correlationId);
    }

    private async Task<string> StartByMemberCodesAsync(
        IReadOnlyCollection<string> memberCodes,
        bool adminOverride,
        bool publishToGateway,
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
            () => StartRunCore(targetCodes, selectedMembers.Select(static x => x.Name).ToArray(), publishToGateway),
            adminOverride);
    }

    private string StartByTargetCodes(IReadOnlyCollection<string> targetCodes, bool adminOverride, bool publishToGateway)
    {
        return _taskAccessService.ExecuteDeferredStart(
            WorkflowTaskCodes.Run,
            () => StartRunCore(targetCodes, memberNames: null, publishToGateway),
            adminOverride);
    }

    private string StartRunCore(IReadOnlyCollection<string> targetCodes, IReadOnlyCollection<string>? memberNames, bool publishToGateway)
    {
        var normalizedCodes = NormalizeTargetCodes(targetCodes);
        var outputDirectory = Path.GetFullPath(_runOptions.OutputDirectory);
        var request = _requestFactory.Create(normalizedCodes, outputDirectory, publishToGateway);

        if (!_runWorkflow.TryActivate(request.CorrelationId, request))
        {
            throw new InvalidOperationException("Run activation conflict.");
        }

        try
        {
            _runStateStore.SetStarted(
                request.CorrelationId,
                normalizedCodes,
                memberNames?.Where(static x => !string.IsNullOrWhiteSpace(x))
                    .Select(static x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? Array.Empty<string>(),
                publishToGateway);
        }
        catch
        {
            _runWorkflow.Complete(request.CorrelationId);
            throw;
        }

        return request.CorrelationId;
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
