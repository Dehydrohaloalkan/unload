namespace Unload.Core;

/// <summary>
/// Контракт публикации сообщений в шлюз отправки.
/// </summary>
public interface IGatewayPublisher
{
    Task PublishFileBatchReadyAsync(SenderFileBatchReadyEvent @event, CancellationToken cancellationToken);

    Task PublishSenderFeedbackAsync(SenderFileDispatchFeedback feedback, CancellationToken cancellationToken);
}
