namespace Unload.Core;

public interface IGatewaySenderFeedbackConsumer
{
    Task ConsumeAsync(SenderFileDispatchFeedback feedback, CancellationToken cancellationToken);
}
