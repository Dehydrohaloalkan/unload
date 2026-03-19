using Microsoft.AspNetCore.SignalR;
using Unload.Api.ErrorHandling;
using Unload.Core;
using Unload.Run.Application;
using Unload.TaskFlow;
using Unload.Workflow;

namespace Unload.Api.UseCases;

public interface IStartRunUseCase
{
    Task<RunAcceptedResponse> ExecuteAsync(RunStartRequest request, CancellationToken cancellationToken);
}

public sealed class StartRunUseCase : IStartRunUseCase
{
    private readonly IWorkflowTaskDispatcher _dispatcher;
    private readonly IRunStateStore _runStateStore;
    private readonly IHubContext<RunStatusHub> _hubContext;
    private readonly ILogger<StartRunUseCase> _logger;

    public StartRunUseCase(
        IWorkflowTaskDispatcher dispatcher,
        IRunStateStore runStateStore,
        IHubContext<RunStatusHub> hubContext,
        ILogger<StartRunUseCase> logger)
    {
        _dispatcher = dispatcher;
        _runStateStore = runStateStore;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task<RunAcceptedResponse> ExecuteAsync(RunStartRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _dispatcher.DispatchAsync<StartRunTaskRequest, StartRunTaskResult>(
                WorkflowTaskCodes.Run,
                new StartRunTaskRequest(request.MemberCodes, RunSelectionMode.MemberCodes),
                cancellationToken);

            _logger.LogInformation("Run accepted. CorrelationId: {CorrelationId}", result.CorrelationId);

            var runState = _runStateStore.Get(result.CorrelationId);
            if (runState is not null)
            {
                await _hubContext.Clients.All.SendAsync("run_status", runState, cancellationToken);
            }

            return new RunAcceptedResponse(
                result.CorrelationId,
                $"/api/runs/{result.CorrelationId}",
                "/hubs/status",
                "SubscribeRun",
                "status",
                "run_status",
                $"/api/runs/{result.CorrelationId}/stop");
        }
        catch (WorkflowTaskDispatchException ex)
        {
            _logger.LogWarning("Run launch rejected. Code: {ErrorCode}, Message: {Message}", ex.ErrorCode, ex.Message);
            throw new ApiProblemException(
                ex.FailureKind == WorkflowTaskFailureKind.Validation
                    ? StatusCodes.Status400BadRequest
                    : StatusCodes.Status409Conflict,
                ex.FailureKind == WorkflowTaskFailureKind.Validation
                    ? "Validation error"
                    : "Run conflict",
                ex.Message,
                ex.ErrorCode,
                ex.Extensions);
        }
    }
}
