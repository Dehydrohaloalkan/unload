namespace Unload.Core;

public interface IGatewaySenderFeedbackSource
{
    IAsyncEnumerable<SenderFileDispatchFeedback> ReadSenderFeedbackAsync(CancellationToken cancellationToken);
}
