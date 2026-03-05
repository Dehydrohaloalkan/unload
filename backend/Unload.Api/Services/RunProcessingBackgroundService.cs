using Microsoft.AspNetCore.SignalR;
using Unload.Application;
using Unload.Core;

namespace Unload.Api;

/// <summary>
/// Фоновый обработчик запусков API.
/// Используется для запуска раннера, обновления статусов и отправки SignalR-событий клиентам.
/// </summary>
public class RunProcessingBackgroundService : BackgroundService
{
    private readonly IRunCoordinator _runCoordinator;
    private readonly IRunStateStore _runStateStore;
    private readonly IRunner _runner;
    private readonly IHubContext<RunStatusHub> _hubContext;
    private readonly ILogger<RunProcessingBackgroundService> _logger;

    /// <summary>
    /// Создает фоновый обработчик с зависимостями диспетчера запусков, раннера и SignalR.
    /// </summary>
    /// <param name="runCoordinator">Диспетчер запросов на выполнение.</param>
    /// <param name="runStateStore">Хранилище состояний запусков.</param>
    /// <param name="runner">Движок выполнения выгрузки.</param>
    /// <param name="hubContext">Контекст SignalR hub для отправки событий.</param>
    /// <param name="logger">Логгер фонового сервиса.</param>
    public RunProcessingBackgroundService(
        IRunCoordinator runCoordinator,
        IRunStateStore runStateStore,
        IRunner runner,
        IHubContext<RunStatusHub> hubContext,
        ILogger<RunProcessingBackgroundService> logger)
    {
        _runCoordinator = runCoordinator;
        _runStateStore = runStateStore;
        _runner = runner;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Основной цикл обработки запусков.
    /// </summary>
    /// <param name="stoppingToken">Токен остановки фонового сервиса.</param>
    /// <returns>Задача жизненного цикла фонового сервиса.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _runCoordinator.ReadActivationsAsync(stoppingToken))
        {
            _runStateStore.SetRunning(request.CorrelationId);
            await PublishRunStateAsync(request.CorrelationId, stoppingToken);

            try
            {
                await foreach (var @event in _runner.RunAsync(request, stoppingToken))
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Run '{CorrelationId}' failed in background worker.", request.CorrelationId);
                _runStateStore.SetFailed(request.CorrelationId, ex.Message);
                await PublishRunStateAsync(request.CorrelationId, stoppingToken);
            }
            finally
            {
                _runCoordinator.Complete(request.CorrelationId);
            }
        }
    }

    /// <summary>
    /// Публикует агрегированный статус запуска всем подключенным клиентам.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <param name="cancellationToken">Токен отмены отправки.</param>
    /// <returns>Задача завершения публикации.</returns>
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
