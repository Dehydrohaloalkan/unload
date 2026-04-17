using Unload.Core;
using Unload.Run.Application;

namespace Unload.Run.Runtime;

public sealed class MqSenderFeedbackConsumer : IMqSenderFeedbackConsumer
{
    private readonly IRunStateStore _runStateStore;

    public MqSenderFeedbackConsumer(IRunStateStore runStateStore)
    {
        _runStateStore = runStateStore;
    }

    public Task ConsumeAsync(SenderFileDispatchFeedback feedback, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _runStateStore.ApplySenderFeedback(feedback);
        return Task.CompletedTask;
    }
}
