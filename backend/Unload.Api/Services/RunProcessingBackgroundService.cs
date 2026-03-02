using Microsoft.AspNetCore.SignalR;
using Unload.Core;

namespace Unload.Api;

public class RunProcessingBackgroundService : BackgroundService
{
    private readonly IRunQueue _runQueue;
    private readonly IRunStateStore _runStateStore;
    private readonly IRunner _runner;
    private readonly IHubContext<RunStatusHub> _hubContext;
    private readonly ILogger<RunProcessingBackgroundService> _logger;

    public RunProcessingBackgroundService(
        IRunQueue runQueue,
        IRunStateStore runStateStore,
        IRunner runner,
        IHubContext<RunStatusHub> hubContext,
        ILogger<RunProcessingBackgroundService> logger)
    {
        _runQueue = runQueue;
        _runStateStore = runStateStore;
        _runner = runner;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _runQueue.DequeueAllAsync(stoppingToken))
        {
            _runStateStore.SetRunning(request.CorrelationId);
            await PublishRunStateAsync(request.CorrelationId, stoppingToken);

            try
            {
                await foreach (var @event in _runner.RunAsync(request, stoppingToken))
                {
                    _runStateStore.ApplyEvent(@event);

                    await _hubContext.Clients
                        .Group(@event.CorrelationId)
                        .SendAsync("status", @event, stoppingToken);

                    await PublishRunStateAsync(@event.CorrelationId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Run '{CorrelationId}' failed in background worker.", request.CorrelationId);
                _runStateStore.SetFailed(request.CorrelationId, ex.Message);
                await PublishRunStateAsync(request.CorrelationId, stoppingToken);
            }
        }
    }

    private async Task PublishRunStateAsync(string correlationId, CancellationToken cancellationToken)
    {
        var state = _runStateStore.Get(correlationId);
        if (state is null)
        {
            return;
        }

        await _hubContext.Clients.All.SendAsync("run_status", state, cancellationToken);
    }
}
