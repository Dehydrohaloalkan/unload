namespace Unload.Core;

/// <summary>
/// Контракт публикации сообщений в транспорт MQ.
/// </summary>
public interface IMqPublisher
{
    Task PublishFileBatchReadyAsync(SenderFileBatchReadyEvent @event, CancellationToken cancellationToken);

    Task PublishSenderFeedbackAsync(SenderFileDispatchFeedback feedback, CancellationToken cancellationToken);
}
