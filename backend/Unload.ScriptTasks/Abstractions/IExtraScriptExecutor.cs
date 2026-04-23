using System.Collections.Concurrent;
using Unload.ScriptTasks.Models;

namespace Unload.ScriptTasks.Abstractions;

public interface IExtraScriptExecutor
{
    Task<ExtraScriptExecutionResult> ExecuteAsync(
        string scriptPath,
        string correlationId,
        ConcurrentDictionary<string, ConcurrentQueue<string>> aggregatedLines,
        CancellationToken cancellationToken);
}

