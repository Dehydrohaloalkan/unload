using System.Collections.Concurrent;

namespace Unload.ScriptTasks;

public interface IExtraScriptExecutor
{
    Task<ExtraScriptExecutionResult> ExecuteAsync(
        string scriptPath,
        string correlationId,
        ConcurrentDictionary<string, ConcurrentQueue<string>> aggregatedLines,
        CancellationToken cancellationToken);
}

