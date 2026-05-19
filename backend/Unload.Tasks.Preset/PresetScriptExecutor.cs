using Microsoft.Extensions.Logging;
using Unload.Core;

namespace Unload.Tasks.Preset;

/// <summary>
/// Выполняет один preset SQL-скрипт через <see cref="IDatabaseClientFactory"/>.
/// </summary>
public class PresetScriptExecutor(
    IDatabaseClientFactory databaseClientFactory,
    ILogger<PresetScriptExecutor> logger)
{
    private readonly IDatabaseClientFactory _databaseClientFactory = databaseClientFactory;
    private readonly ILogger<PresetScriptExecutor> _logger = logger;

    public async Task ExecuteAsync(string scriptPath, string correlationId, CancellationToken cancellationToken)
    {
        var scriptCode = Path.GetFileNameWithoutExtension(scriptPath);
        _logger.LogDebug(
            "Preset script started. CorrelationId: {CorrelationId}, ScriptCode: {ScriptCode}",
            correlationId,
            scriptCode);

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
            await DisposeClientAsync(client);
        }

        _logger.LogDebug(
            "Preset script completed. CorrelationId: {CorrelationId}, ScriptCode: {ScriptCode}",
            correlationId,
            scriptCode);
    }

    private static async Task DisposeClientAsync(IDatabaseClient client)
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
