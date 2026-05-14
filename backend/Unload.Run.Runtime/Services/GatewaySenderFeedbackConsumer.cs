using Unload.Core;
using Unload.Run.Application;

namespace Unload.Run.Runtime;

public class GatewaySenderFeedbackConsumer(IRunStateStore runStateStore) : IGatewaySenderFeedbackConsumer
{
    private readonly IRunStateStore _runStateStore = runStateStore;

    public Task ConsumeAsync(SenderFileDispatchFeedback feedback, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _runStateStore.ApplySenderFeedback(feedback);
        return Task.CompletedTask;
    }
}
