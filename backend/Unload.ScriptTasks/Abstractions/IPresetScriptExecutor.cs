namespace Unload.ScriptTasks.Abstractions;

public interface IPresetScriptExecutor
{
    Task ExecuteAsync(string scriptPath, string correlationId, CancellationToken cancellationToken);
}

