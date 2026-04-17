namespace Unload.Core;

public interface IMqSenderFeedbackConsumer
{
    Task ConsumeAsync(SenderFileDispatchFeedback feedback, CancellationToken cancellationToken);
}
