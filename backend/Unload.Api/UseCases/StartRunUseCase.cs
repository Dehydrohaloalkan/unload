using Microsoft.AspNetCore.SignalR;
using Unload.Api.ErrorHandling;
using Unload.Api.Models;
using Unload.Api.UseCases.Abstractions;
using Unload.Store;
using Unload.Tasks;

namespace Unload.Api.UseCases;

public class StartRunUseCase(
    TaskWorkflow taskWorkflow,
    RunStateStore runStateStore,
    IHubContext<RunStatusHub> hubContext,
    ILogger<StartRunUseCase> logger) : IStartRunUseCase
{
    private readonly TaskWorkflow _taskWorkflow = taskWorkflow;
    private readonly RunStateStore _runStateStore = runStateStore;
    private readonly IHubContext<RunStatusHub> _hubContext = hubContext;
    private readonly ILogger<StartRunUseCase> _logger = logger;

    public async Task<RunAcceptedResponse> ExecuteAsync(RunStartRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var selectedTargetCodes = request.TargetCodes?
                .Where(static code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
            var selectionMode = selectedTargetCodes.Length > 0
                ? RunSelectionMode.TargetCodes
                : RunSelectionMode.MemberCodes;
            var selectedCodes = selectionMode == RunSelectionMode.TargetCodes
                ? selectedTargetCodes
                : request.MemberCodes;

            var result = await _taskWorkflow.LaunchAsync(
                new TaskLaunchRequest(
                    TaskCode: TaskCodes.Run,
                    AdminOverride: request.AdminOverride,
                    PublishToGateway: request.PublishToGateway,
                    Codes: selectedCodes,
                    SelectionMode: selectionMode),
                cancellationToken);

            _logger.LogInformation("Run accepted. CorrelationId: {CorrelationId}", result.ExecutionId);

            var runState = _runStateStore.Get(result.ExecutionId);
            if (runState is not null)
            {
                await _hubContext.Clients.All.SendAsync("run_status", runState, cancellationToken);
            }

            return new RunAcceptedResponse(
                result.ExecutionId,
                $"/api/runs/{result.ExecutionId}",
                "/hubs/status",
                "SubscribeRun",
                "status",
                "run_status",
                $"/api/runs/{result.ExecutionId}/stop");
        }
        catch (TaskLaunchException ex)
        {
            _logger.LogWarning("Run launch rejected. Code: {ErrorCode}, Message: {Message}", ex.ErrorCode, ex.Message);
            throw TaskLaunchExceptions.ToApiProblem(ex, "Run conflict");
        }
    }
}
