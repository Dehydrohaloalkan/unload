using System.Collections.Concurrent;
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
    private readonly IPresetScriptExecutor _presetScriptExecutor;
    private readonly IExtraScriptExecutor _extraScriptExecutor;
    private readonly IExtraOutputWriter _extraOutputWriter;
    private readonly IScriptTaskEventPublisher _eventPublisher;
    private readonly ILogger<ScriptTaskOrchestrator> _logger;
    private readonly SemaphoreSlim _presetSemaphore = new(1, 1);
    private readonly SemaphoreSlim _extraSemaphore = new(1, 1);

    public ScriptTaskOrchestrator(
        string scriptsDirectory,
        string outputDirectory,
        IPresetScriptExecutor presetScriptExecutor,
        IExtraScriptExecutor extraScriptExecutor,
        IExtraOutputWriter extraOutputWriter,
        IScriptTaskEventPublisher eventPublisher,
        ILogger<ScriptTaskOrchestrator> logger)
    {
        _scriptsDirectory = Path.GetFullPath(scriptsDirectory);
        _outputDirectory = Path.GetFullPath(outputDirectory);
        _presetScriptExecutor = presetScriptExecutor;
        _extraScriptExecutor = extraScriptExecutor;
        _extraOutputWriter = extraOutputWriter;
        _eventPublisher = eventPublisher;
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
                await _eventPublisher.PublishAsync(
                    correlationId,
                    RunnerStep.Completed,
                    "Preset task completed: no scripts found.",
                    cancellationToken);
                return new ScriptTaskRunResult(
                    TaskName: "preset",
                    CorrelationId: correlationId,
                    ScriptsExecuted: 0,
                    FilesWritten: 0,
                    OutputPath: null,
                    Message: "No preset scripts found.");
            }

            var tasks = scripts.Select(path => _presetScriptExecutor.ExecuteAsync(path, correlationId, cancellationToken));
            await Task.WhenAll(tasks);
            await _eventPublisher.PublishAsync(
                correlationId,
                RunnerStep.Completed,
                $"Preset task completed. Scripts: {scripts.Length}.",
                cancellationToken);
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
                await _eventPublisher.PublishAsync(
                    correlationId,
                    RunnerStep.Completed,
                    "Extra task completed: no scripts found in scripts root.",
                    cancellationToken);
                return new ScriptTaskRunResult(
                    TaskName: "extra",
                    CorrelationId: correlationId,
                    ScriptsExecuted: 0,
                    FilesWritten: 0,
                    OutputPath: null,
                    Message: "No root scripts found.");
            }

            var aggregatedLines = new ConcurrentDictionary<string, ConcurrentQueue<string>>(StringComparer.OrdinalIgnoreCase);
            var tasks = scripts.Select(path =>
                _extraScriptExecutor.ExecuteAsync(path, correlationId, aggregatedLines, cancellationToken));
            await Task.WhenAll(tasks);

            var writeResult = await _extraOutputWriter.WriteAsync(
                _outputDirectory,
                correlationId,
                aggregatedLines,
                cancellationToken);

            await _eventPublisher.PublishAsync(
                correlationId,
                RunnerStep.Completed,
                $"Extra task completed. Files: {writeResult.FilesWritten}.",
                cancellationToken);
            _logger.LogInformation(
                "Extra task completed. CorrelationId: {CorrelationId}, ScriptsExecuted: {ScriptsExecuted}, FilesWritten: {FilesWritten}, OutputPath: {OutputPath}",
                correlationId,
                scripts.Length,
                writeResult.FilesWritten,
                writeResult.OutputPath);

            return new ScriptTaskRunResult(
                TaskName: "extra",
                CorrelationId: correlationId,
                ScriptsExecuted: scripts.Length,
                FilesWritten: writeResult.FilesWritten,
                OutputPath: writeResult.OutputPath,
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

    private static string BuildCorrelationId(string prefix)
    {
        return $"{prefix}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..43];
    }
}
