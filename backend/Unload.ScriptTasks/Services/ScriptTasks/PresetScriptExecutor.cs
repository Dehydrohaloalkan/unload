using Microsoft.Extensions.Logging;
using Unload.Core;

namespace Unload.ScriptTasks;

public interface IPresetScriptExecutor
{
    Task ExecuteAsync(string scriptPath, string correlationId, CancellationToken cancellationToken);
}

public sealed class PresetScriptExecutor : IPresetScriptExecutor
{
    private readonly IDatabaseClientFactory _databaseClientFactory;
    private readonly IScriptTaskEventPublisher _eventPublisher;
    private readonly ILogger<PresetScriptExecutor> _logger;

    public PresetScriptExecutor(
        IDatabaseClientFactory databaseClientFactory,
        IScriptTaskEventPublisher eventPublisher,
        ILogger<PresetScriptExecutor> logger)
    {
        _databaseClientFactory = databaseClientFactory;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task ExecuteAsync(string scriptPath, string correlationId, CancellationToken cancellationToken)
    {
        var scriptCode = Path.GetFileNameWithoutExtension(scriptPath);
        _logger.LogDebug("Preset script started. CorrelationId: {CorrelationId}, ScriptCode: {ScriptCode}", correlationId, scriptCode);
        var sql = await File.ReadAllTextAsync(scriptPath, cancellationToken);
        await _eventPublisher.PublishAsync(
            correlationId,
            RunnerStep.QueryStarted,
            $"Preset script started: {scriptCode}.",
            cancellationToken,
            scriptCode: scriptCode);

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

        await _eventPublisher.PublishAsync(
            correlationId,
            RunnerStep.QueryCompleted,
            $"Preset script completed: {scriptCode}.",
            cancellationToken,
            scriptCode: scriptCode);
        _logger.LogDebug("Preset script completed. CorrelationId: {CorrelationId}, ScriptCode: {ScriptCode}", correlationId, scriptCode);
    }
}
