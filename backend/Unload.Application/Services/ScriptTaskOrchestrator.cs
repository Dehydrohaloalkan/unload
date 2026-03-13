using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Unload.Core;

namespace Unload.Application;

/// <summary>
/// Выполняет доп-задачи на SQL-скриптах вне каталожного пайплайна.
/// </summary>
public sealed class ScriptTaskOrchestrator : IScriptTaskOrchestrator
{
    private readonly string _scriptsDirectory;
    private readonly string _outputDirectory;
    private readonly IDatabaseClientFactory _databaseClientFactory;
    private readonly IMqPublisher _mqPublisher;
    private readonly ILogger<ScriptTaskOrchestrator> _logger;
    private readonly SemaphoreSlim _presetSemaphore = new(1, 1);
    private readonly SemaphoreSlim _extraSemaphore = new(1, 1);

    public ScriptTaskOrchestrator(
        string scriptsDirectory,
        string outputDirectory,
        IDatabaseClientFactory databaseClientFactory,
        IMqPublisher mqPublisher,
        ILogger<ScriptTaskOrchestrator> logger)
    {
        _scriptsDirectory = Path.GetFullPath(scriptsDirectory);
        _outputDirectory = Path.GetFullPath(outputDirectory);
        _databaseClientFactory = databaseClientFactory;
        _mqPublisher = mqPublisher;
        _logger = logger;
    }

    public async Task<ScriptTaskRunResult> RunPresetAsync(CancellationToken cancellationToken)
    {
        if (!await _presetSemaphore.WaitAsync(0, cancellationToken))
        {
            _logger.LogWarning("Preset task launch rejected: another preset task is already running.");
            throw new InvalidOperationException("Preset task is already running.");
        }

        try
        {
            _logger.LogInformation("Preset task started. ScriptsRoot: {ScriptsDirectory}", _scriptsDirectory);
            var presetDirectory = Path.Combine(_scriptsDirectory, "preset");
            if (!Directory.Exists(presetDirectory))
            {
                throw new DirectoryNotFoundException($"Preset scripts directory was not found: {presetDirectory}");
            }

            var scripts = Directory
                .EnumerateFiles(presetDirectory, "*.sql", SearchOption.TopDirectoryOnly)
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var correlationId = BuildCorrelationId("preset");
            if (scripts.Length == 0)
            {
                _logger.LogInformation("Preset task finished with no scripts. CorrelationId: {CorrelationId}", correlationId);
                await PublishAsync(correlationId, RunnerStep.Completed, "Preset task completed: no scripts found.", cancellationToken);
                return new ScriptTaskRunResult(
                    TaskName: "preset",
                    CorrelationId: correlationId,
                    ScriptsExecuted: 0,
                    FilesWritten: 0,
                    OutputPath: null,
                    Message: "No preset scripts found.");
            }

            var tasks = scripts.Select(path => ExecutePresetScriptAsync(path, correlationId, cancellationToken));
            await Task.WhenAll(tasks);
            await PublishAsync(correlationId, RunnerStep.Completed, $"Preset task completed. Scripts: {scripts.Length}.", cancellationToken);
            _logger.LogInformation(
                "Preset task completed. CorrelationId: {CorrelationId}, ScriptsExecuted: {ScriptsExecuted}",
                correlationId,
                scripts.Length);

            return new ScriptTaskRunResult(
                TaskName: "preset",
                CorrelationId: correlationId,
                ScriptsExecuted: scripts.Length,
                FilesWritten: 0,
                OutputPath: null,
                Message: "Preset scripts executed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preset task failed.");
            throw;
        }
        finally
        {
            _presetSemaphore.Release();
        }
    }

    public async Task<ScriptTaskRunResult> RunExtraAsync(CancellationToken cancellationToken)
    {
        if (!await _extraSemaphore.WaitAsync(0, cancellationToken))
        {
            _logger.LogWarning("Extra task launch rejected: another extra task is already running.");
            throw new InvalidOperationException("Extra scripts task is already running.");
        }

        try
        {
            _logger.LogInformation("Extra task started. ScriptsRoot: {ScriptsDirectory}", _scriptsDirectory);
            if (!Directory.Exists(_scriptsDirectory))
            {
                throw new DirectoryNotFoundException($"Scripts directory was not found: {_scriptsDirectory}");
            }

            var scripts = Directory
                .EnumerateFiles(_scriptsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var correlationId = BuildCorrelationId("extra");
            if (scripts.Length == 0)
            {
                _logger.LogInformation("Extra task finished with no scripts. CorrelationId: {CorrelationId}", correlationId);
                await PublishAsync(correlationId, RunnerStep.Completed, "Extra task completed: no scripts found in scripts root.", cancellationToken);
                return new ScriptTaskRunResult(
                    TaskName: "extra",
                    CorrelationId: correlationId,
                    ScriptsExecuted: 0,
                    FilesWritten: 0,
                    OutputPath: null,
                    Message: "No root scripts found.");
            }

            var aggregatedLines = new ConcurrentDictionary<string, ConcurrentQueue<string>>(StringComparer.OrdinalIgnoreCase);
            var tasks = scripts.Select(path => ExecuteExtraScriptAsync(path, correlationId, aggregatedLines, cancellationToken));
            await Task.WhenAll(tasks);

            var runDirectory = CreateRunDirectory(_outputDirectory);
            var filesDirectory = Path.Combine(runDirectory, "output-files");
            Directory.CreateDirectory(filesDirectory);

            var filesWritten = 0;
            foreach (var item in aggregatedLines.OrderBy(static x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                var bankKey = SanitizeFileNameSegment(item.Key);
                var filePath = Path.Combine(filesDirectory, $"{bankKey}.txt");
                var lines = item.Value.ToArray();
                await File.WriteAllLinesAsync(filePath, lines, cancellationToken);
                filesWritten++;

                await PublishAsync(
                    correlationId,
                    RunnerStep.FileWritten,
                    $"Extra file written: {Path.GetFileName(filePath)}.",
                    cancellationToken,
                    targetCode: "EXTRA",
                    records: lines.Length,
                    filePath: filePath);
            }

            await PublishAsync(correlationId, RunnerStep.Completed, $"Extra task completed. Files: {filesWritten}.", cancellationToken);
            _logger.LogInformation(
                "Extra task completed. CorrelationId: {CorrelationId}, ScriptsExecuted: {ScriptsExecuted}, FilesWritten: {FilesWritten}, OutputPath: {OutputPath}",
                correlationId,
                scripts.Length,
                filesWritten,
                runDirectory);

            return new ScriptTaskRunResult(
                TaskName: "extra",
                CorrelationId: correlationId,
                ScriptsExecuted: scripts.Length,
                FilesWritten: filesWritten,
                OutputPath: runDirectory,
                Message: "Extra scripts executed and files created.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Extra task failed.");
            throw;
        }
        finally
        {
            _extraSemaphore.Release();
        }
    }

    private async Task ExecutePresetScriptAsync(string scriptPath, string correlationId, CancellationToken cancellationToken)
    {
        var scriptCode = Path.GetFileNameWithoutExtension(scriptPath);
        _logger.LogDebug("Preset script started. CorrelationId: {CorrelationId}, ScriptCode: {ScriptCode}", correlationId, scriptCode);
        var sql = await File.ReadAllTextAsync(scriptPath, cancellationToken);
        await PublishAsync(correlationId, RunnerStep.QueryStarted, $"Preset script started: {scriptCode}.", cancellationToken, scriptCode: scriptCode);

        var client = _databaseClientFactory.CreateClient();
        try
        {
            await using var reader = await client.GetDataReaderAsync(sql, cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                // Для preset-задачи важен факт выполнения скрипта.
            }
        }
        finally
        {
            await DisposeClientAsync(client);
        }

        await PublishAsync(correlationId, RunnerStep.QueryCompleted, $"Preset script completed: {scriptCode}.", cancellationToken, scriptCode: scriptCode);
        _logger.LogDebug("Preset script completed. CorrelationId: {CorrelationId}, ScriptCode: {ScriptCode}", correlationId, scriptCode);
    }

    private async Task ExecuteExtraScriptAsync(
        string scriptPath,
        string correlationId,
        ConcurrentDictionary<string, ConcurrentQueue<string>> aggregatedLines,
        CancellationToken cancellationToken)
    {
        var scriptCode = Path.GetFileNameWithoutExtension(scriptPath);
        _logger.LogDebug("Extra script started. CorrelationId: {CorrelationId}, ScriptCode: {ScriptCode}", correlationId, scriptCode);
        var sql = await File.ReadAllTextAsync(scriptPath, cancellationToken);
        await PublishAsync(correlationId, RunnerStep.QueryStarted, $"Extra script started: {scriptCode}.", cancellationToken, targetCode: "EXTRA", scriptCode: scriptCode);

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
            await DisposeClientAsync(client);
        }

        await PublishAsync(
            correlationId,
            RunnerStep.QueryCompleted,
            $"Extra script completed: {scriptCode}.",
            cancellationToken,
            targetCode: "EXTRA",
            scriptCode: scriptCode,
            records: records);
        _logger.LogDebug(
            "Extra script completed. CorrelationId: {CorrelationId}, ScriptCode: {ScriptCode}, Records: {Records}",
            correlationId,
            scriptCode,
            records);
    }

    private async Task PublishAsync(
        string correlationId,
        RunnerStep step,
        string message,
        CancellationToken cancellationToken,
        string? targetCode = null,
        string? scriptCode = null,
        int? records = null,
        string? filePath = null)
    {
        var @event = new RunnerEvent(
            OccurredAt: DateTimeOffset.UtcNow,
            CorrelationId: correlationId,
            Step: step,
            Message: message,
            TargetCode: targetCode,
            ScriptCode: scriptCode,
            Records: records,
            FilePath: filePath);
        await _mqPublisher.PublishAsync(@event, cancellationToken);
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

        throw new InvalidOperationException(
            $"Result set does not contain required column '{columnName}'.");
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

    private static string BuildCorrelationId(string prefix)
    {
        return $"{prefix}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..43];
    }

    private static string CreateRunDirectory(string baseOutputDirectory)
    {
        Directory.CreateDirectory(baseOutputDirectory);
        var timestamp = DateTime.Now.ToString("dd_MM_yyyy_HHmmss");
        var candidate = Path.Combine(baseOutputDirectory, $"{timestamp}_extra");
        var index = 1;

        while (Directory.Exists(candidate))
        {
            candidate = Path.Combine(baseOutputDirectory, $"{timestamp}_extra_{index:D2}");
            index++;
        }

        Directory.CreateDirectory(candidate);
        return candidate;
    }

    private static string SanitizeFileNameSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "UNKNOWN";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "UNKNOWN" : sanitized;
    }
}
