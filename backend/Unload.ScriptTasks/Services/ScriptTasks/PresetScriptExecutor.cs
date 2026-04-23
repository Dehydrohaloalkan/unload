using Microsoft.Extensions.Logging;
using Unload.Core;
using Unload.ScriptTasks.Abstractions;

namespace Unload.ScriptTasks;

public  class PresetScriptExecutor(
    IDatabaseClientFactory databaseClientFactory,
    ILogger<PresetScriptExecutor> logger) : IPresetScriptExecutor
{
    private readonly IDatabaseClientFactory _databaseClientFactory = databaseClientFactory;
    private readonly ILogger<PresetScriptExecutor> _logger = logger;

    public async Task ExecuteAsync(string scriptPath, string correlationId, CancellationToken cancellationToken)
    {
        var scriptCode = Path.GetFileNameWithoutExtension(scriptPath);
        _logger.LogDebug("Preset script started. CorrelationId: {CorrelationId}, ScriptCode: {ScriptCode}", correlationId, scriptCode);
        var sql = await File.ReadAllTextAsync(scriptPath, cancellationToken);

        var client = _databaseClientFactory.CreateClient();
        try
        {
            await using var reader = await client.GetDataReaderAsync(sql, cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
            }
        }
        finally
        {
            await ScriptTaskDatabaseClientDisposer.DisposeAsync(client);
        }
        _logger.LogDebug("Preset script completed. CorrelationId: {CorrelationId}, ScriptCode: {ScriptCode}", correlationId, scriptCode);
    }
}
