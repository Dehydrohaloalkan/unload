using Unload.Core;

namespace Unload.Store;

public class GatewaySenderFeedbackConsumer(RunStateStore runStateStore) : IGatewaySenderFeedbackConsumer
{
    private readonly RunStateStore _runStateStore = runStateStore;

    public Task ConsumeAsync(SenderFileDispatchFeedback feedback, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _runStateStore.ApplySenderFeedback(feedback);
        return Task.CompletedTask;
    }
}
