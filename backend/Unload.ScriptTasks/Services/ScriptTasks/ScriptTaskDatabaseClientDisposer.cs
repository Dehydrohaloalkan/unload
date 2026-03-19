using Unload.Core;

namespace Unload.ScriptTasks;

internal static class ScriptTaskDatabaseClientDisposer
{
    public static async Task DisposeAsync(IDatabaseClient client)
    {
        if (client is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
            return;
        }

        if (client is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
