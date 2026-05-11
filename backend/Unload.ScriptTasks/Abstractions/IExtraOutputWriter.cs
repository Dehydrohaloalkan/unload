using System.Collections.Concurrent;
using Unload.ScriptTasks.Models;

namespace Unload.ScriptTasks.Abstractions;

public interface IExtraOutputWriter
{
    Task<ExtraOutputWriteResult> WriteAsync(
        string baseOutputDirectory,
        string correlationId,
        ConcurrentDictionary<string, ConcurrentQueue<string>> aggregatedLines,
        bool publishToMq,
        CancellationToken cancellationToken);
}

