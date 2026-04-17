namespace Unload.Core;

public interface IMqSenderFeedbackSource
{
    IAsyncEnumerable<SenderFileDispatchFeedback> ReadSenderFeedbackAsync(CancellationToken cancellationToken);
}
