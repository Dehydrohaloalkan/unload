using Microsoft.Extensions.Logging;
using Unload.ScriptTasks.Abstractions;
using Unload.Tasks;

namespace Unload.ScriptTasks;

/// <summary>
/// Выполняет preset-задачу на SQL-скриптах вне каталожного пайплайна.
/// </summary>
public class ScriptTaskOrchestrator(
    string scriptsDirectory,
    IPresetScriptExecutor presetScriptExecutor,
    IScriptTaskEventPublisher eventPublisher,
    ILogger<ScriptTaskOrchestrator> logger) : IScriptTaskOrchestrator
{
    private readonly string _scriptsDirectory = Path.GetFullPath(scriptsDirectory);
    private readonly IPresetScriptExecutor _presetScriptExecutor = presetScriptExecutor;
    private readonly IScriptTaskEventPublisher _eventPublisher = eventPublisher;
    private readonly ILogger<ScriptTaskOrchestrator> _logger = logger;
    private readonly SemaphoreSlim _presetSemaphore = new(1, 1);

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

    private static string BuildCorrelationId(string prefix)
    {
        return $"{prefix}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..43];
    }
}
