using Microsoft.AspNetCore.SignalR;
using Unload.Core;
using Unload.Store;
using Unload.Tasks;
using Unload.Tasks.MainUnload;

namespace Unload.Api.Services;

/// <summary>
/// Фоновый обработчик запусков API.
/// Используется для запуска раннера, обновления статусов и отправки SignalR-событий клиентам.
/// </summary>
public class RunProcessingBackgroundService(
    RunActivationChannel runWorkflow,
    RunStateStore runStateStore,
    TaskExecutionHistoryStore taskExecutionHistoryStore,
    MainUnloadEngine runner,
    IHubContext<RunStatusHub> hubContext,
    ILogger<RunProcessingBackgroundService> logger) : BackgroundService
{
    private readonly RunActivationChannel _runWorkflow = runWorkflow;
    private readonly RunStateStore _runStateStore = runStateStore;
    private readonly TaskExecutionHistoryStore _taskExecutionHistoryStore = taskExecutionHistoryStore;
    private readonly MainUnloadEngine _runner = runner;
    private readonly IHubContext<RunStatusHub> _hubContext = hubContext;
    private readonly ILogger<RunProcessingBackgroundService> _logger = logger;

    /// <summary>
    /// Основной цикл обработки запусков.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var activation in _runWorkflow.ReadActivationsAsync(stoppingToken))
        {
            var request = activation.Payload;
            using var runCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, activation.CancellationToken);
            var runToken = runCts.Token;
            _runStateStore.SetRunning(request.CorrelationId);
            await PublishRunStateAsync(request.CorrelationId, stoppingToken);
            _logger.LogInformation("Run moved to Running. CorrelationId: {CorrelationId}", request.CorrelationId);

            try
            {
                await foreach (var @event in _runner.RunAsync(request, runToken))
                {
                    _runStateStore.ApplyEvent(@event);

                    await _hubContext.Clients
                        .All
                        .SendAsync("status", @event, stoppingToken);

                    await PublishRunStateAsync(@event.CorrelationId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                _runStateStore.SetCancelled(request.CorrelationId, "Run was cancelled by user.");
                await PublishRunStateAsync(request.CorrelationId, stoppingToken);
                _logger.LogInformation("Run cancelled by user. CorrelationId: {CorrelationId}", request.CorrelationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Run '{CorrelationId}' failed in background worker.", request.CorrelationId);
                _runStateStore.SetFailed(request.CorrelationId, ex.Message);
                await PublishRunStateAsync(request.CorrelationId, stoppingToken);
            }
            finally
            {
                var finalState = _runStateStore.Get(request.CorrelationId);
                if (finalState is not null &&
                    finalState.Status == RunLifecycleStatus.Running &&
                    finalState.LastStep == RunnerStep.Completed)
                {
                    // Runner finished, but MQ sender may still be dispatching artifacts.
                    // Keep run in "active" until sender feedback promotes it to a terminal status.
                    finalState = await WaitForTerminalStateAsync(request.CorrelationId, stoppingToken);
                }

                if (finalState is not null)
                {
                    if (finalState.Status == RunLifecycleStatus.Completed)
                    {
                        _taskExecutionHistoryStore.Add(
                            TaskCodes.Run,
                            finalState.CreatedAt,
                            finalState.UpdatedAt,
                            finalState.CorrelationId,
                            finalState.Message,
                            scriptsExecuted: null,
                            filesWritten: finalState.OutputArtifacts?.Count ?? 0,
                            outputPath: finalState.OutputPath);
                    }

                    _logger.LogInformation(
                        "Run finished. CorrelationId: {CorrelationId}, Status: {Status}",
                        request.CorrelationId,
                        finalState.Status);
                }

                _runWorkflow.Complete(request.CorrelationId);
            }
        }
    }

    private async Task<RunStatusInfo?> WaitForTerminalStateAsync(string correlationId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var current = _runStateStore.Get(correlationId);
            if (current is null)
            {
                return null;
            }

            if (current.Status is RunLifecycleStatus.Completed or RunLifecycleStatus.Failed or RunLifecycleStatus.Cancelled)
            {
                return current;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        return _runStateStore.Get(correlationId);
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
