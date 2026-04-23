namespace Unload.ScriptTasks;

public interface IPresetScriptExecutor
{
    Task ExecuteAsync(string scriptPath, string correlationId, CancellationToken cancellationToken);
}

