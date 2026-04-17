using Microsoft.AspNetCore.SignalR;
using Unload.Core;
using Unload.Run.Application;

namespace Unload.Api;

/// <summary>
/// Проецирует feedback sender-а в состояние run и уведомления SignalR.
/// </summary>
public sealed class SenderFeedbackProjectionBackgroundService : BackgroundService
{
    private readonly IMqSenderFeedbackSource _feedbackSource;
    private readonly IMqSenderFeedbackConsumer _feedbackConsumer;
    private readonly IRunStateStore _runStateStore;
    private readonly IHubContext<RunStatusHub> _hubContext;
    private readonly ILogger<SenderFeedbackProjectionBackgroundService> _logger;

    public SenderFeedbackProjectionBackgroundService(
        IMqSenderFeedbackSource feedbackSource,
        IMqSenderFeedbackConsumer feedbackConsumer,
        IRunStateStore runStateStore,
        IHubContext<RunStatusHub> hubContext,
        ILogger<SenderFeedbackProjectionBackgroundService> logger)
    {
        _feedbackSource = feedbackSource;
        _feedbackConsumer = feedbackConsumer;
        _runStateStore = runStateStore;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var feedback in _feedbackSource.ReadSenderFeedbackAsync(stoppingToken))
        {
            try
            {
                await _feedbackConsumer.ConsumeAsync(feedback, stoppingToken);
                var state = _runStateStore.Get(feedback.CorrelationId);
                if (state is not null)
                {
                    await _hubContext.Clients.All.SendAsync("run_status", state, stoppingToken);
                }
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
}
