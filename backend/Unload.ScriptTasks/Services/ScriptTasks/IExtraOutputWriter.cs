using System.Collections.Concurrent;

namespace Unload.ScriptTasks;

public interface IExtraOutputWriter
{
    Task<ExtraOutputWriteResult> WriteAsync(
        string baseOutputDirectory,
        string correlationId,
        ConcurrentDictionary<string, ConcurrentQueue<string>> aggregatedLines,
        CancellationToken cancellationToken);
}

