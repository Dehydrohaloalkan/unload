using Microsoft.AspNetCore.SignalR;
using Unload.Api.ErrorHandling;
using Unload.Api.Models;
using Unload.Api.UseCases.Abstractions;
using Unload.Core;
using Unload.Run.Application;
using Unload.TaskFlow;
using Unload.TaskFlow.Exceptions;
using Unload.Workflow;

namespace Unload.Api.UseCases;

public class StartRunUseCase(
    IWorkflowTaskDispatcher dispatcher,
    IRunStateStore runStateStore,
    IHubContext<RunStatusHub> hubContext,
    ILogger<StartRunUseCase> logger) : IStartRunUseCase
{
    private readonly IWorkflowTaskDispatcher _dispatcher = dispatcher;
    private readonly IRunStateStore _runStateStore = runStateStore;
    private readonly IHubContext<RunStatusHub> _hubContext = hubContext;
    private readonly ILogger<StartRunUseCase> _logger = logger;

    public async Task<RunAcceptedResponse> ExecuteAsync(RunStartRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _dispatcher.DispatchAsync<StartRunTaskRequest, StartRunTaskResult>(
                WorkflowTaskCodes.Run,
                new StartRunTaskRequest(request.MemberCodes, RunSelectionMode.MemberCodes, request.AdminOverride),
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
            throw WorkflowDispatchExceptions.ToApiProblem(ex, "Run conflict");
        }
    }
}
