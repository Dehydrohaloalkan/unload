using Microsoft.AspNetCore.SignalR;
using Unload.Core;
using Unload.Store;

namespace Unload.Api.Services;

/// <summary>
/// Проецирует feedback sender-а в состояние run и уведомления SignalR.
/// </summary>
public class SenderFeedbackProjectionBackgroundService(
    IGatewaySenderFeedbackSource feedbackSource,
    IGatewaySenderFeedbackConsumer feedbackConsumer,
    RunStateStore runStateStore,
    IHubContext<RunStatusHub> hubContext,
    ILogger<SenderFeedbackProjectionBackgroundService> logger) : BackgroundService
{
    private readonly IGatewaySenderFeedbackSource _feedbackSource = feedbackSource;
    private readonly IGatewaySenderFeedbackConsumer _feedbackConsumer = feedbackConsumer;
    private readonly RunStateStore _runStateStore = runStateStore;
    private readonly IHubContext<RunStatusHub> _hubContext = hubContext;
    private readonly ILogger<SenderFeedbackProjectionBackgroundService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var feedback in _feedbackSource.ReadSenderFeedbackAsync(stoppingToken))
            {
                try
                {
                    await _feedbackConsumer.ConsumeAsync(feedback, stoppingToken);
                    var state = _runStateStore.Get(feedback.CorrelationId);
                    if (state is not null)
                    {
                        await _hubContext.Clients.All.SendRunStatusAsync(state, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to project sender feedback. CorrelationId: {CorrelationId}, BatchId: {BatchId}, Kind: {Kind}",
                        feedback.CorrelationId,
                        feedback.BatchId,
                        feedback.Kind);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Sender feedback projection stopped.");
        }
    }
}
