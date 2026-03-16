using Unload.Core;

namespace Unload.Application;

public interface IScriptTaskEventPublisher
{
    Task PublishAsync(
        string correlationId,
        RunnerStep step,
        string message,
        CancellationToken cancellationToken,
        string? targetCode = null,
        string? scriptCode = null,
        int? records = null,
        string? filePath = null);
}

public sealed class ScriptTaskEventPublisher : IScriptTaskEventPublisher
{
    private readonly IMqPublisher _mqPublisher;

    public ScriptTaskEventPublisher(IMqPublisher mqPublisher)
    {
        _mqPublisher = mqPublisher;
    }

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
        var @event = new RunnerEvent(
            OccurredAt: DateTimeOffset.UtcNow,
            CorrelationId: correlationId,
            Step: step,
            Message: message,
            TargetCode: targetCode,
            ScriptCode: scriptCode,
            Records: records,
            FilePath: filePath);
        await _mqPublisher.PublishAsync(@event, cancellationToken);
    }
}
