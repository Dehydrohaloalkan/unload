namespace Unload.Core;

public interface IDatabaseClient
{
    IAsyncEnumerable<DatabaseRow> ExecuteScriptAsync(
        ScriptDefinition script,
        CancellationToken cancellationToken);
}
