using Unload.Core;
using Unload.ScriptTasks.Abstractions;

namespace Unload.ScriptTasks;

public class ScriptTaskEventPublisher(IMqPublisher mqPublisher) : IScriptTaskEventPublisher
{
    private readonly IMqPublisher _mqPublisher = mqPublisher;

    public async Task PublishAsync(
        string correlationId,
        RunnerStep step,
        string message,
        CancellationToken cancellationToken,
        string? targetCode = null,
        string? scriptCode = null,
        int? records = null,
        string? filePath = null)
    {
        await Task.CompletedTask;
    }

    public Task PublishFileBatchReadyAsync(
        string correlationId,
        string memberName,
        IReadOnlyCollection<SenderFileDescriptor> files,
        CancellationToken cancellationToken)
    {
        var @event = new SenderFileBatchReadyEvent(
            OccurredAt: DateTimeOffset.UtcNow,
            CorrelationId: correlationId,
            MemberName: memberName,
            BatchId: $"{correlationId}:{memberName}",
            Version: 1,
            Files: files);
        return _mqPublisher.PublishFileBatchReadyAsync(@event, cancellationToken);
    }
}
