using System.Text.RegularExpressions;
using Unload.Catalog;
using Unload.Core;
using Unload.Store;
using Unload.Tasks;

namespace Unload.Tasks.MainUnload;

/// <summary>
/// Задача основной выгрузки (main run).
/// Deferred: стартует фоновую обработку через <see cref="RunActivationChannel"/> и возвращает Accepted.
/// Фиксацию завершения делает фоновый воркер (<c>RunProcessingBackgroundService</c>), не эта задача.
/// </summary>
public class MainUnloadTask(
    ICatalogService catalogService,
    RunRequestFactory requestFactory,
    RunActivationChannel runWorkflow,
    RunStateStore runStateStore,
    RunApplicationOptions runOptions) : UnloadTask
{
    private static readonly Regex TargetCodePattern = new("^[A-Z0-9_]{3,64}$", RegexOptions.Compiled);

    private readonly ICatalogService _catalogService = catalogService;
    private readonly RunRequestFactory _requestFactory = requestFactory;
    private readonly RunActivationChannel _runWorkflow = runWorkflow;
    private readonly RunStateStore _runStateStore = runStateStore;
    private readonly RunApplicationOptions _runOptions = runOptions;

    public override string Code => TaskCodes.Run;

    public override IReadOnlyCollection<string> RequiresCompleted => [TaskCodes.Preset];

    public override IReadOnlyCollection<string> ConflictsWith => [TaskCodes.Preset];

    public override bool RequiresDailyWindowOpen => true;

    public override bool IsDeferred => true;

    public override async Task<TaskExecutionResult> ExecuteAsync(
        TaskLaunchRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Codes is null)
        {
            throw new TaskLaunchException(
                TaskLaunchFailureKind.Validation,
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
            throw new TaskLaunchException(
                TaskLaunchFailureKind.Validation,
                "VALIDATION_ERROR",
                "At least one code is required.");
        }

        var correlationId = request.SelectionMode switch
        {
            RunSelectionMode.MemberCodes => await StartByMemberCodesAsync(
                normalizedCodes, request.PublishToGateway, cancellationToken),
            RunSelectionMode.TargetCodes => StartByTargetCodes(
                normalizedCodes, request.PublishToGateway),
            _ => throw new TaskLaunchException(
                TaskLaunchFailureKind.Validation,
                "VALIDATION_ERROR",
                $"Unsupported run selection mode '{request.SelectionMode}'.")
        };

        return new TaskExecutionResult(
            TaskCode: Code,
            ExecutionId: correlationId,
            Status: TaskExecutionStatus.Accepted,
            Message: "Run accepted.");
    }

    private async Task<string> StartByMemberCodesAsync(
        IReadOnlyCollection<string> memberCodes,
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
            throw new TaskLaunchException(
                TaskLaunchFailureKind.Validation,
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
            throw new TaskLaunchException(
                TaskLaunchFailureKind.Validation,
                "TARGET_CODES_NOT_FOUND",
                "No target codes found for selected members.");
        }

        return StartRunCore(
            targetCodes,
            selectedMembers.Select(static x => x.Name).ToArray(),
            publishToGateway);
    }

    private string StartByTargetCodes(IReadOnlyCollection<string> targetCodes, bool publishToGateway)
    {
        return StartRunCore(targetCodes, memberNames: null, publishToGateway);
    }

    private string StartRunCore(
        IReadOnlyCollection<string> targetCodes,
        IReadOnlyCollection<string>? memberNames,
        bool publishToGateway)
    {
        var normalizedCodes = NormalizeTargetCodes(targetCodes);
        var outputDirectory = Path.GetFullPath(_runOptions.OutputDirectory);
        var runRequest = _requestFactory.Create(normalizedCodes, outputDirectory, publishToGateway);

        if (!_runWorkflow.TryActivate(runRequest.CorrelationId, runRequest))
        {
            throw new InvalidOperationException("Run activation conflict.");
        }

        try
        {
            _runStateStore.SetStarted(
                runRequest.CorrelationId,
                normalizedCodes,
                memberNames?.Where(static x => !string.IsNullOrWhiteSpace(x))
                    .Select(static x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? Array.Empty<string>(),
                publishToGateway);
        }
        catch
        {
            _runWorkflow.Complete(runRequest.CorrelationId);
            throw;
        }

        return runRequest.CorrelationId;
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
