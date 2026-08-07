using Microsoft.AspNetCore.SignalR;
using Unload.Store;
using Unload.Tasks;
using Unload.Tasks.ExtraUnload;

namespace Unload.Api.Services;

/// <summary>
/// Фоновый обработчик extra-выгрузки. Аналог <see cref="MainUnloadHostedService"/>:
/// читает активации из <see cref="ExtraActivationChannel"/>, гоняет <see cref="ExtraUnloadEngine"/>,
/// обновляет <see cref="RunStateStore"/>, шлёт SignalR-события и фиксирует итог в истории.
/// </summary>
public class ExtraUnloadHostedService(
    ExtraActivationChannel extraWorkflow,
    RunStateStore runStateStore,
    TaskExecutionHistoryStore taskExecutionHistoryStore,
    ExtraUnloadEngine engine,
    IHubContext<RunStatusHub> hubContext,
    ILogger<ExtraUnloadHostedService> logger) : BackgroundService
{
    private readonly ExtraActivationChannel _extraWorkflow = extraWorkflow;
    private readonly RunStateStore _runStateStore = runStateStore;
    private readonly TaskExecutionHistoryStore _taskExecutionHistoryStore = taskExecutionHistoryStore;
    private readonly ExtraUnloadEngine _engine = engine;
    private readonly IHubContext<RunStatusHub> _hubContext = hubContext;
    private readonly ILogger<ExtraUnloadHostedService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ProcessActivationsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Extra unload worker stopped.");
        }
    }

    private async Task ProcessActivationsAsync(CancellationToken stoppingToken)
    {
        await foreach (var activation in _extraWorkflow.ReadActivationsAsync(stoppingToken))
        {
            var request = activation.Payload;
            using var runCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, activation.CancellationToken);
            var runToken = runCts.Token;
            await PublishRunStateAsync(request.CorrelationId, stoppingToken);
            _logger.LogInformation("Extra run started. CorrelationId: {CorrelationId}", request.CorrelationId);

            try
            {
                await foreach (var @event in _engine.RunAsync(request, runToken))
                {
                    _runStateStore.ApplyEvent(@event);
                    await _hubContext.Clients.All.SendStatusAsync(@event, stoppingToken);
                    await PublishRunStateAsync(@event.CorrelationId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                _runStateStore.SetCancelled(request.CorrelationId, "Extra task was cancelled by user.");
                await PublishRunStateAsync(request.CorrelationId, stoppingToken);
                _logger.LogInformation("Extra run cancelled by user. CorrelationId: {CorrelationId}", request.CorrelationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Extra run '{CorrelationId}' failed in background worker.", request.CorrelationId);
                _runStateStore.SetFailed(request.CorrelationId, ex.Message);
                await PublishRunStateAsync(request.CorrelationId, stoppingToken);
            }
            finally
            {
                var finalState = _runStateStore.Get(request.CorrelationId);
                if (finalState is not null &&
                    finalState.Status == RunLifecycleStatus.Running &&
                    finalState.LastStep == Unload.Core.RunnerStep.Completed)
                {
                    // Движок отработал, но шлюз может ещё досылать файлы — ждём терминального статуса.
                    finalState = await WaitForTerminalStateAsync(request.CorrelationId, stoppingToken);
                }

                if (finalState is not null)
                {
                    if (finalState.Status == RunLifecycleStatus.Completed)
                    {
                        _taskExecutionHistoryStore.Add(
                            TaskCodes.Extra,
                            finalState.CreatedAt,
                            finalState.UpdatedAt,
                            finalState.CorrelationId,
                            finalState.Message,
                            scriptsExecuted: finalState.MemberStatuses?.Count ?? 0,
                            filesWritten: finalState.OutputArtifacts?.Count ?? 0,
                            outputPath: finalState.OutputPath);
                    }

                    _logger.LogInformation(
                        "Extra run finished. CorrelationId: {CorrelationId}, Status: {Status}",
                        request.CorrelationId,
                        finalState.Status);
                }

                _extraWorkflow.Complete(request.CorrelationId);
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

            // Стоп во время досылки в шлюз: движок уже отработал, и из CancellationRequested
            // статус сам не станет терминальным (промоушен идёт только из Running) —
            // без явной фиксации отмены ожидание зависло бы навсегда вместе со слотом запуска.
            if (current.Status == RunLifecycleStatus.CancellationRequested)
            {
                _runStateStore.SetCancelled(correlationId, "Extra task was cancelled while waiting for gateway delivery.");
                await PublishRunStateAsync(correlationId, cancellationToken);
                return _runStateStore.Get(correlationId);
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

        await _hubContext.Clients.All.SendRunStatusAsync(state, cancellationToken);
    }
}
