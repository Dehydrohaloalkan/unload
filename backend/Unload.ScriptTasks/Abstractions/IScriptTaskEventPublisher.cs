using Unload.Core;

namespace Unload.ScriptTasks.Abstractions;

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

    Task PublishFileBatchReadyAsync(
        string correlationId,
        string memberName,
        IReadOnlyCollection<SenderFileDescriptor> files,
        CancellationToken cancellationToken);
}

