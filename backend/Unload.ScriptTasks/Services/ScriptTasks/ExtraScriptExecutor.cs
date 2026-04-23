using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Unload.Core;

namespace Unload.ScriptTasks;

public interface IExtraScriptExecutor
{
    Task<ExtraScriptExecutionResult> ExecuteAsync(
        string scriptPath,
        string correlationId,
        ConcurrentDictionary<string, ConcurrentQueue<string>> aggregatedLines,
        CancellationToken cancellationToken);
}

public  class ExtraScriptExecutor : IExtraScriptExecutor
{
    private readonly IDatabaseClientFactory _databaseClientFactory;
    private readonly ILogger<ExtraScriptExecutor> _logger;

    public ExtraScriptExecutor(
        IDatabaseClientFactory databaseClientFactory,
        ILogger<ExtraScriptExecutor> logger)
    {
        _databaseClientFactory = databaseClientFactory;
        _logger = logger;
    }

    public async Task<ExtraScriptExecutionResult> ExecuteAsync(
        string scriptPath,
        string correlationId,
        ConcurrentDictionary<string, ConcurrentQueue<string>> aggregatedLines,
        CancellationToken cancellationToken)
    {
        var scriptCode = Path.GetFileNameWithoutExtension(scriptPath);
        _logger.LogDebug("Extra script started. CorrelationId: {CorrelationId}, ScriptCode: {ScriptCode}", correlationId, scriptCode);
        var sql = await File.ReadAllTextAsync(scriptPath, cancellationToken);

        var client = _databaseClientFactory.CreateClient();
        var records = 0;
        try
        {
            await using var reader = await client.GetDataReaderAsync(sql, cancellationToken);
            var nrBankOrdinal = ResolveOrdinal(reader, "NrBank", 0);
            var lineFileOrdinal = ResolveOrdinal(reader, "LineFile", 1);

            while (await reader.ReadAsync(cancellationToken))
            {
                var nrBank = reader.IsDBNull(nrBankOrdinal)
                    ? "UNKNOWN"
                    : Convert.ToString(reader.GetValue(nrBankOrdinal)) ?? "UNKNOWN";
                var lineFile = reader.IsDBNull(lineFileOrdinal)
                    ? string.Empty
                    : Convert.ToString(reader.GetValue(lineFileOrdinal)) ?? string.Empty;

                var queue = aggregatedLines.GetOrAdd(nrBank, static _ => new ConcurrentQueue<string>());
                queue.Enqueue(lineFile);
                records++;
            }
        }
        finally
        {
            await ScriptTaskDatabaseClientDisposer.DisposeAsync(client);
        }

        _logger.LogDebug(
            "Extra script completed. CorrelationId: {CorrelationId}, ScriptCode: {ScriptCode}, Records: {Records}",
            correlationId,
            scriptCode,
            records);

        return new ExtraScriptExecutionResult(scriptCode, records);
    }

    private static int ResolveOrdinal(DbDataReader reader, string columnName, int fallbackOrdinal)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        if (fallbackOrdinal < reader.FieldCount)
        {
            return fallbackOrdinal;
        }

        throw new InvalidOperationException($"Result set does not contain required column '{columnName}'.");
    }
}

public  record ExtraScriptExecutionResult(string ScriptCode, int Records);
